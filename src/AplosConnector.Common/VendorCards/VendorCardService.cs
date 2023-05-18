using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AplosConnector.Common.Models;
using Microsoft.Extensions.Logging;
using PexCard.Api.Client.Core;
using PexCard.Api.Client.Core.Exceptions;
using PexCard.Api.Client.Core.Models;

namespace AplosConnector.Common.VendorCards
{
    public class VendorCardService : IVendorCardService
    {
        private readonly IPexApiClient _pexApiClient;
        private readonly ILogger _logger;

        private readonly int MaxVendorName = 15;
        private readonly decimal DefaultDailyFundingLimit = 5_000;

        private readonly string SpendingRulesetName = "Aplos Vendor Auto Funding";
        private readonly SpendingRulesetCategoriesModel AllSpendingCategoriesAllowed = new SpendingRulesetCategoriesModel
        {
            CategoryId = 0,
            AssociationsOrganizationsAllowed = true,
            AutomotiveDealersAllowed = true,
            EducationalServicesAllowed = true,
            EntertainmentAllowed = true,
            FuelPumpsAllowed = true,
            GasStationsConvenienceStoresAllowed = true,
            GroceryStoresAllowed = true,
            HealthcareChildcareServicesAllowed = true,
            ProfessionalServicesAllowed = true,
            RestaurantsAllowed = true,
            RetailStoresAllowed = true,
            TravelTransportationAllowed = true,
            HardwareStoresAllowed = true,
        };

        public VendorCardService(IPexApiClient pexApiClient,
                                 ILogger<VendorCardService> logger)
        {
            if (pexApiClient is null)
            {
                throw new ArgumentNullException(nameof(pexApiClient));
            }

            _pexApiClient = pexApiClient;
            _logger = logger;
        }

        public async Task<VendorCardsOrdered> OrderVendorCardsAsync(Pex2AplosMappingModel mapping, IEnumerable<VendorCardOrder> cardOrders, CancellationToken cancelToken = default)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }
            if (cardOrders is null)
            {
                throw new ArgumentNullException(nameof(cardOrders));
            }

            var adminProfileTask = _pexApiClient.GetMyAdminProfile(mapping.PEXExternalAPIToken, cancelToken);
            var autoFundingRulesetTask = cardOrders.Any(x => x.AutoFunding) ? GetOrCreateAplosAutoFundingRuleset(mapping, cancelToken) : Task.FromResult<SpendingRulesetModel>(null);

            await Task.WhenAll(adminProfileTask, autoFundingRulesetTask);

            var adminProfile = adminProfileTask.Result;
            var autoFundingRuleset = autoFundingRulesetTask.Result;

            var orderRequest = new VendorCardCreateOrderRequestModel
            {
                VendorCards = new List<VendorCardOrderItemRequest>()
            };

            foreach (var cardOrder in cardOrders)
            {
                orderRequest.VendorCards.Add(new VendorCardOrderItemRequest
                {
                    AutoActivation = true,
                    RulesetId = cardOrder.AutoFunding ? autoFundingRuleset?.RulesetId : default,
                    FundCardAmount = cardOrder.InitialFunding.HasValue ? Convert.ToDecimal(cardOrder.InitialFunding) : default,
                    GroupId = cardOrder.GroupId,
                    VendorName = cardOrder.Name.Substring(0, Math.Min(cardOrder.Name.Length, MaxVendorName)),
                    Email = adminProfile.Admin.Email,
                    Phone = adminProfile.Admin.Phone
                });
            }

            _logger.LogInformation("Requesting creation of {VendorCardsCount} vendor cards...", orderRequest.VendorCards.Count);
            var vendorCardsOrderResult = await _pexApiClient.CreateVendorCardOrder(mapping.PEXExternalAPIToken, orderRequest, cancelToken);
            _logger.LogInformation("{VendorCardsRequested} vendor cards were requested to create. OrderId={VendorCardsOrderId}.", vendorCardsOrderResult.NumberOfCardsRequested, vendorCardsOrderResult.VendorCardOrderId);

            var vendorCardsOrdered = new VendorCardsOrdered(vendorCardsOrderResult.VendorCardOrderId)
            {
                CardOrders = cardOrders.Select(cardOrder => new VendorCardOrdered(vendorCardsOrderResult.VendorCardOrderId, cardOrder.Id, cardOrder.Name, cardOrder.AutoFunding, cardOrder.InitialFunding, cardOrder.GroupId)).ToList(),
            };

            return vendorCardsOrdered;
        }

        private async Task<SpendingRulesetModel> GetOrCreateAplosAutoFundingRuleset(Pex2AplosMappingModel mapping, CancellationToken cancelToken)
        {
            if (mapping is null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            GetSpendingRulesetsResponseModel rulesets = null;

            try
            {
                rulesets = await _pexApiClient.GetSpendingRulesets(mapping.PEXExternalAPIToken, cancelToken);
            }
            catch (PexApiClientException ex) when (ex.Code == System.Net.HttpStatusCode.NotFound)
            {
            }

            var businessSettings = await _pexApiClient.GetBusinessSettings(mapping.PEXExternalAPIToken, cancelToken);

            var ruleset = rulesets?.SpendingRulesets.FirstOrDefault(x => x.Name == SpendingRulesetName);
            if (ruleset == null)
            {
                var createRuleset = new CreateSpendingRulesetRequestModel
                {
                    Name = SpendingRulesetName,
                    UsePexAccountBalanceForAuths = true,
                    DailySpendLimit = decimal.Round(businessSettings.DefaultDailyLimit ?? DefaultDailyFundingLimit, 2, MidpointRounding.AwayFromZero),
                    CardPresence = CardPresence.OnlyCardNotPresent,
                    CardNotPresentAllowed = true,
                    InternationalAllowed = false,
                    UseCustomerAuthDecision = false,
                    SpendingRulesetCategories = AllSpendingCategoriesAllowed
                };

                _logger.LogInformation("A spend ruleset with name '{SpendingRulesetName}' doesn't exist. Creating RuleSet:\n{@SpendRuleSet}", SpendingRulesetName, createRuleset);

                var createResult = await _pexApiClient.CreateSpendingRuleset(mapping.PEXExternalAPIToken, createRuleset, cancelToken);

                _logger.LogInformation("Created an auto funding rule set for vendor cards with name '{SpendingRulesetName}' doesn't exist. Created RuleSet:\n{@SpendRuleSet}.", SpendingRulesetName, createRuleset);

                ruleset = (await _pexApiClient.GetSpendingRuleset(mapping.PEXExternalAPIToken, createResult.RulesetId, cancelToken))?.SpendingRuleset;
            }
            else
            {
                _logger.LogInformation("A spend ruleset with name '{SpendingRulesetName}' already exists.", SpendingRulesetName);
            }

            return ruleset;
        }
    }
}
