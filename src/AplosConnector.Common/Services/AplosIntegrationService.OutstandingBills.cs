using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Const;
using AplosConnector.Common.Enums;
using AplosConnector.Common.Models;
using AplosConnector.Common.Models.Aplos;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core.Enums;
using PexCard.Api.Client.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AplosConnector.Common.Services
{
    public partial class AplosIntegrationService
    {
        internal const string AplosVendorCustomIdPrefix = "APLOS";

        public async Task<List<AplosOutstandingBillModel>> GetAplosOutstandingBills(Pex2AplosMappingModel mapping, DateOnly startDate, CancellationToken cancellationToken)
        {
            var payables = await GetAplosPayables(mapping, startDate, cancellationToken);
            return AplosPayableFilter.SelectUnpaid(payables)
                .Select(AplosPayableFilter.ToOutstandingBill)
                .ToList();
        }

        internal async Task SyncOutstandingBills(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            DateTime utcNow,
            CancellationToken cancellationToken)
        {
            if (!mapping.SyncOutstandingBills) return;

            await RefreshBusinessSettings(mapping, cancellationToken);

            if (!mapping.UseBillPayEnabled)
            {
                logger.LogInformation($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. Bill Pay is disabled for this business account.");
                return;
            }

            if (!mapping.SyncOutstandingBills)
            {
                logger.LogInformation($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. Outstanding bill sync is turned off.");
                return;
            }

            var startDateUtc = GetStartDateUtc(mapping, utcNow, _syncSettings);
            var endDateUtc = GetEndDateUtc(mapping.EndDateUtc, utcNow);
            var (startDate, endDate) = GetEstDayWindow(startDateUtc, endDateUtc);

            if (startDate.Date >= endDate.Date)
            {
                logger.LogInformation($"Skipping sync outstanding bills for business {mapping.PEXBusinessAcctId}. Empty sync window {startDate:yyyy-MM-dd}..{endDate:yyyy-MM-dd}.");
                return;
            }

            var syncCount = 0;
            var failureCount = 0;
            var failureNotes = new List<string>();

            try
            {
                // f_rangestart is date-only and read as a local calendar day, so a UTC-day start drops bills in the gap.
                var payables = await GetAplosPayables(mapping, startDateUtc.ToEstCalendarDate(), cancellationToken);
                // Only f_rangestart is sent, and Aplos ignores filters it doesn't honour. A bill dated outside the window
                // would miss the bill-inbox dedup search and import every run, so the window is enforced here too.
                var firstBillDate = startDateUtc.ToEstCalendarDate();
                var lastBillDate = endDateUtc.ToEstCalendarDate();
                var unpaid = AplosPayableFilter.SelectUnpaid(payables)
                    .Where(payable => DateOnly.FromDateTime(payable.BillDate) >= firstBillDate
                                      && DateOnly.FromDateTime(payable.BillDate) <= lastBillDate)
                    .ToList();
                logger.LogInformation($"Retrieved {payables.Count} Aplos payables ({unpaid.Count} unpaid) for business {mapping.PEXBusinessAcctId} from {startDate:yyyy-MM-dd}.");

                var syncedNotes = await GetExistingAplosBillInboxNotes(mapping, startDate, endDate, cancellationToken);
                var newBills = unpaid
                    .Where(payable => !syncedNotes.Any(note => note.Contains(GetAplosBillSyncedNote(payable.Id), StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                logger.LogInformation($"Found {newBills.Count} new Aplos payables to create in the PEX bill inbox for business {mapping.PEXBusinessAcctId}.");

                if (newBills.Count > 0)
                {
                    var pexVendors = await _pexApiClient.GetVendors(mapping.PEXExternalAPIToken, cancellationToken);
                    var (vendorsByName, vendorsByCustomId) = BuildPexVendorLookups(pexVendors?.Vendors);

                    var businessDetails = await _pexApiClient.GetBusinessDetails(mapping.PEXExternalAPIToken, cancellationToken);
                    var vendorCardAcctIdByName = (businessDetails?.CHAccountList ?? new List<CardholderAccountModel>())
                        .Where(ch => string.Equals(ch.CardholderType, CardholderType.Vendor.ToString(), StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(ch.AccountStatus, "OPEN", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(ch => $"{ch.LastName}".Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().AccountId, StringComparer.OrdinalIgnoreCase);

                    // Phase 1: resolve or create the PEX vendor behind each Aplos contact. The reason a contact
                    // can't be used (missing address vs email) is captured here, where the Aplos contact is still
                    // in hand, so phase 3 can fail that bill with the field name instead of a generic message.
                    var newlyCreatedVendorIds = new List<int>();
                    var unresolvedContactMessages = new Dictionary<int, string>();
                    foreach (var contactId in newBills.Select(b => b.Contact?.Id ?? 0).Distinct())
                    {
                        try
                        {
                            await ResolvePexVendorForAplosContact(logger, mapping, contactId, vendorsByName, vendorsByCustomId, newlyCreatedVendorIds, unresolvedContactMessages, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            unresolvedContactMessages.TryAdd(contactId, "Could not create or approve the PEX vendor for this contact.");
                            logger.LogWarning(ex, $"Failed to resolve PEX vendor for Aplos contact {contactId} for business {mapping.PEXBusinessAcctId}.");
                        }
                    }

                    if (newlyCreatedVendorIds.Count > 0)
                    {
                        try
                        {
                            await BatchCreateAndLinkVendorCards(logger, mapping, vendorsByName, vendorsByCustomId, newlyCreatedVendorIds, vendorCardAcctIdByName, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"Failed to batch-create vendor cards for business {mapping.PEXBusinessAcctId}.");
                        }
                    }

                    foreach (var payable in newBills)
                    {
                        try
                        {
                            var pexVendor = FindPexVendorForPayable(payable, vendorsByName, vendorsByCustomId);
                            if (pexVendor == null)
                            {
                                failureCount++;
                                var contactName = AplosPayableFilter.GetContactName(payable.Contact);
                                var reason = unresolvedContactMessages.TryGetValue(payable.Contact?.Id ?? 0, out var message)
                                    ? message
                                    : $"Add a billing address for the {contactName} contact in Aplos.";
                                failureNotes.Add($"Bill {payable.ReferenceNumber ?? payable.Id}: {reason}");
                                logger.LogWarning($"Could not resolve a PEX vendor for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}. {reason}");
                                continue;
                            }

                            await CreatePexBillInbox(logger, mapping, payable, pexVendor, cancellationToken);
                            syncCount++;
                        }
                        catch (Exception ex)
                        {
                            failureCount++;
                            failureNotes.Add($"Bill {payable.ReferenceNumber ?? payable.Id}: {ex.Message}");
                            logger.LogError(ex, $"Failed to create a PEX bill inbox item for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failureCount++;
                failureNotes.Add(ex.Message);
                logger.LogError(ex, $"Failed to sync outstanding bills for business {mapping.PEXBusinessAcctId}.");
            }

            SyncStatus syncStatus;
            if (failureCount == 0)
            {
                syncStatus = SyncStatus.Success;
            }
            else if (syncCount != 0)
            {
                syncStatus = SyncStatus.Partial;
            }
            else
            {
                syncStatus = SyncStatus.Failed;
            }

            var result = new SyncResultModel
            {
                PEXBusinessAcctId = mapping.PEXBusinessAcctId,
                SyncType = SyncTypes.OutstandingBills,
                SyncStatus = syncStatus.ToString(),
                SyncedRecords = syncCount,
                SyncNotes = failureCount == 0 ? string.Empty : string.Join(" ", failureNotes)
            };
            await _historyStorage.CreateAsync(result, cancellationToken);
        }

        internal static string GetAplosBillSyncedNote(string payableId) => $"Synced Aplos bill #{payableId} to PEX";

        internal static string BuildAplosVendorCustomId(int aplosContactId) => $"{AplosVendorCustomIdPrefix}{aplosContactId}";

        internal static (Dictionary<string, VendorModel> byName, Dictionary<string, VendorModel> byCustomId) BuildPexVendorLookups(IReadOnlyList<VendorModel> vendors)
        {
            vendors ??= new List<VendorModel>();

            var byName = vendors
                .Where(v => !string.IsNullOrEmpty(v.VendorName))
                .GroupBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var byCustomId = new Dictionary<string, VendorModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var vendor in vendors)
            {
                if (string.IsNullOrEmpty(vendor.CustomId)) continue;
                foreach (var id in vendor.CustomId.Split(','))
                {
                    var trimmed = id.Trim();
                    if (trimmed.Length > 0) byCustomId[trimmed] = vendor;
                }
            }

            return (byName, byCustomId);
        }

        internal static bool TryBuildVendorAddress(AplosApiContactDetail contact, out VendorAddressModel vendorAddress, out List<string> missingFields)
        {
            vendorAddress = null;
            missingFields = new List<string>();

            var address = contact?.Addresses?.FirstOrDefault(a => a.IsPrimary) ?? contact?.Addresses?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(address?.Street1)) missingFields.Add("Street 1");
            if (string.IsNullOrWhiteSpace(address?.City)) missingFields.Add("City");
            if (string.IsNullOrWhiteSpace(address?.State)) missingFields.Add("State");
            if (string.IsNullOrWhiteSpace(address?.PostalCode)) missingFields.Add("Postal Code");

            if (missingFields.Count > 0) return false;

            vendorAddress = new VendorAddressModel
            {
                AddressLine1 = address.Street1.Trim(),
                AddressLine2 = address.Street2?.Trim(),
                City = address.City.Trim(),
                State = address.State.Trim(),
                PostalCode = address.PostalCode.Trim()
            };
            return true;
        }

        internal static string GetContactEmail(AplosApiContactDetail contact)
        {
            if (contact == null) return null;
            if (!string.IsNullOrWhiteSpace(contact.Email)) return contact.Email.Trim();

            var email = contact.Emails?.FirstOrDefault(e => e.IsPrimary && !string.IsNullOrWhiteSpace(e.Address))
                        ?? contact.Emails?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Address));
            return email?.Address?.Trim();
        }

        private VendorModel FindPexVendorForPayable(
            AplosApiPayableDetail payable,
            Dictionary<string, VendorModel> vendorsByName,
            Dictionary<string, VendorModel> vendorsByCustomId)
        {
            var contactId = payable.Contact?.Id ?? 0;
            if (contactId != 0 && vendorsByCustomId.TryGetValue(BuildAplosVendorCustomId(contactId), out var vendor))
            {
                return vendor;
            }

            var contactName = AplosPayableFilter.GetContactName(payable.Contact);
            if (!string.IsNullOrEmpty(contactName) && vendorsByName.TryGetValue(contactName, out vendor))
            {
                return vendor;
            }

            return null;
        }

        private async Task ResolvePexVendorForAplosContact(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            int aplosContactId,
            Dictionary<string, VendorModel> vendorsByName,
            Dictionary<string, VendorModel> vendorsByCustomId,
            List<int> newlyCreatedVendorIds,
            Dictionary<int, string> unresolvedContactMessages,
            CancellationToken cancellationToken)
        {
            if (aplosContactId == 0)
            {
                unresolvedContactMessages[aplosContactId] = "Assign a contact to this bill in Aplos.";
                return;
            }

            var customId = BuildAplosVendorCustomId(aplosContactId);
            if (vendorsByCustomId.ContainsKey(customId))
            {
                return;
            }

            // The payables list endpoint returns a contact stub without addresses or emails, so the full
            // contact has to be read before the PEX vendor can be built.
            var aplosApiClient = MakeAplosApiClient(mapping);
            var contact = (await aplosApiClient.GetContact(aplosContactId, cancellationToken))?.Data?.Contact;
            if (contact == null)
            {
                unresolvedContactMessages[aplosContactId] = "Could not retrieve the contact from Aplos.";
                return;
            }

            var contactName = AplosPayableFilter.GetContactName(contact);
            if (string.IsNullOrEmpty(contactName))
            {
                unresolvedContactMessages[aplosContactId] = "Add a Company Name or First and Last Name to this contact in Aplos.";
                return;
            }

            if (vendorsByName.ContainsKey(contactName))
            {
                return;
            }

            if (!TryBuildVendorAddress(contact, out var vendorAddress, out var missingFields))
            {
                unresolvedContactMessages[aplosContactId] = $"Add a billing address for the {contactName} contact in Aplos. Missing: {string.Join(", ", missingFields)}.";
                return;
            }

            // Bill Pay delivers vendor-card payments by email, so a vendor without one would be created here
            // and fail later at payment time. Fail the bill now with something the user can act on.
            var email = GetContactEmail(contact);
            if (string.IsNullOrWhiteSpace(email))
            {
                unresolvedContactMessages[aplosContactId] = $"Add an Email for the {contactName} contact in Aplos.";
                return;
            }

            var createVendorRequest = new CreateVendorRequestModel
            {
                VendorName = contactName,
                VendorCardPaymentEnabled = true,
                EmailForRemittance = email,
                VendorAddress = vendorAddress,
                CustomId = customId
            };

            var pexVendor = await _pexApiClient.CreateVendor(mapping.PEXExternalAPIToken, createVendorRequest, cancellationToken);
            pexVendor = await _pexApiClient.ApproveVendor(mapping.PEXExternalAPIToken, pexVendor.VendorId, cancellationToken);

            logger.LogInformation($"Created and approved PEX vendor {pexVendor.VendorId} from Aplos contact {aplosContactId} for business {mapping.PEXBusinessAcctId}.");

            vendorsByName[pexVendor.VendorName] = pexVendor;
            vendorsByCustomId[customId] = pexVendor;
            newlyCreatedVendorIds.Add(pexVendor.VendorId);
        }

        // PEX vendor card names are capped at 15 characters, the same limit VendorCardService applies.
        private const int MaxVendorCardName = 15;

        private static string ToVendorCardName(string vendorName)
            => vendorName.Length <= MaxVendorCardName ? vendorName : vendorName.Substring(0, MaxVendorCardName);

        private async Task BatchCreateAndLinkVendorCards(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            Dictionary<string, VendorModel> vendorsByName,
            Dictionary<string, VendorModel> vendorsByCustomId,
            List<int> newlyCreatedVendorIds,
            Dictionary<string, int> vendorCardAcctIdByName,
            CancellationToken cancellationToken)
        {
            var vendorsNeedingCards = vendorsByName.Values
                .Where(v => newlyCreatedVendorIds.Contains(v.VendorId))
                .Where(v => !vendorCardAcctIdByName.TryGetValue(ToVendorCardName(v.VendorName), out var acctId) || acctId <= 0)
                .ToList();

            if (vendorsNeedingCards.Count == 0) return;

            var adminProfile = await _pexApiClient.GetMyAdminProfile(mapping.PEXExternalAPIToken, cancellationToken);
            var cardOrderRequest = new VendorCardCreateOrderRequestModel
            {
                VendorCards = vendorsNeedingCards.Select(v => new VendorCardOrderItemRequest
                {
                    VendorName = ToVendorCardName(v.VendorName),
                    AutoActivation = true,
                    Email = adminProfile?.Admin?.Email,
                    Phone = adminProfile?.Admin?.Phone
                }).ToList()
            };

            logger.LogInformation($"Ordering {cardOrderRequest.VendorCards.Count} vendor cards in batch for business {mapping.PEXBusinessAcctId}.");
            var cardOrderResult = await _pexApiClient.CreateVendorCardOrder(mapping.PEXExternalAPIToken, cardOrderRequest, cancellationToken);

            var cardOrder = await _pexApiClient.GetVendorCardOrder(mapping.PEXExternalAPIToken, cardOrderResult.VendorCardOrderId, cancellationToken);
            if (cardOrder?.Cards == null)
            {
                logger.LogWarning($"Vendor card order {cardOrderResult.VendorCardOrderId} returned no cards for business {mapping.PEXBusinessAcctId}.");
                return;
            }

            var cardAcctIdByVendorName = cardOrder.Cards
                .Where(c => c.AcctId.HasValue && !string.IsNullOrEmpty(c.VendorName))
                .GroupBy(c => c.VendorName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().AcctId.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var vendor in vendorsNeedingCards)
            {
                if (!cardAcctIdByVendorName.TryGetValue(ToVendorCardName(vendor.VendorName), out var cardAcctId))
                {
                    logger.LogWarning($"No vendor card created for PEX vendor {vendor.VendorId} for business {mapping.PEXBusinessAcctId}.");
                    continue;
                }

                try
                {
                    await _pexApiClient.AddVendorCard(mapping.PEXExternalAPIToken, vendor.VendorId, new AddVendorCardRequestModel { CardholderAcctId = cardAcctId }, cancellationToken);
                    var updatedVendor = await _pexApiClient.SetDefaultVendorCard(mapping.PEXExternalAPIToken, vendor.VendorId, cardAcctId, cancellationToken);

                    if (updatedVendor != null)
                    {
                        vendorsByName[vendor.VendorName] = updatedVendor;
                        if (!string.IsNullOrEmpty(vendor.CustomId))
                        {
                            vendorsByCustomId[vendor.CustomId] = updatedVendor;
                        }
                    }
                    vendorCardAcctIdByName[ToVendorCardName(vendor.VendorName)] = cardAcctId;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"Failed to link vendor card {cardAcctId} to PEX vendor {vendor.VendorId} for business {mapping.PEXBusinessAcctId}.");
                }
            }
        }

        private async Task CreatePexBillInbox(
            ILogger logger,
            Pex2AplosMappingModel mapping,
            AplosApiPayableDetail payable,
            VendorModel pexVendor,
            CancellationToken cancellationToken)
        {
            var request = new CreateBillInboxRequestModel
            {
                Source = BillInboxSource.Aplos,
                VendorId = pexVendor.VendorId,
                VendorName = pexVendor.VendorName,
                Amount = AplosPayableFilter.NormalizeAmount(payable.Amount),
                BillDate = payable.BillDate,
                DueDate = payable.DueDate,
                BillNumber = payable.ReferenceNumber
            };

            var billInbox = await _pexApiClient.CreateBillInbox(mapping.PEXExternalAPIToken, request, cancellationToken);
            logger.LogInformation($"Created PEX bill inbox item {billInbox.BillInboxId} for Aplos payable {payable.Id} for business {mapping.PEXBusinessAcctId}.");

            if (billInbox.MetadataId.HasValue)
            {
                var noteText = $"{GetAplosBillSyncedNote(payable.Id)} with ID #{billInbox.BillInboxId} on {DateTime.UtcNow:O}.";
                await _pexApiClient.AddTransactionRelationshipNote(mapping.PEXExternalAPIToken, billInbox.MetadataId.Value, noteText, cancellationToken);
            }
            else
            {
                logger.LogWarning($"PEX bill inbox item {billInbox.BillInboxId} has no metadata id; the Aplos payable {payable.Id} audit note was not written and this bill may import again.");
            }
        }

        private async Task<List<string>> GetExistingAplosBillInboxNotes(
            Pex2AplosMappingModel mapping,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            // Aplos bill_date is date-only; once persisted to PEX it lands at midnight, so the filter is anchored
            // to EST calendar days to match the f_rangestart used when the payables were read.
            var request = new SearchBillInboxRequestModel
            {
                Source = BillInboxSource.Aplos,
                BillDateFrom = startDate.Date,
                BillDateTo = endDate.Date.AddDays(1).AddTicks(-1)
            };

            const int pageSize = 100;
            var page = 1;
            var fetched = 0;
            var notes = new List<string>();

            while (true)
            {
                var response = await _pexApiClient.SearchBillInbox(mapping.PEXExternalAPIToken, request, page, pageSize, cancellationToken);
                if (response?.Items == null || response.Items.Count == 0) break;

                foreach (var note in response.Items.SelectMany(item => item.Metadata?.Notes ?? Enumerable.Empty<TransactionNoteModel>()))
                {
                    if (!string.IsNullOrEmpty(note.NoteText)) notes.Add(note.NoteText);
                }

                fetched += response.Items.Count;
                if (response.Items.Count < pageSize || response.PageInfo == null || fetched >= response.PageInfo.TotalItems) break;

                page++;
            }

            return notes;
        }
    }
}
