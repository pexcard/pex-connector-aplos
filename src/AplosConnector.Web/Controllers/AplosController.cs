using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AplosConnector.Core.Storages;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using AplosConnector.Common.Models.Aplos;
using AplosConnector.Common.Services.Abstractions;
using Aplos.Api.Client.Models.Response;
using System.Threading;
using Aplos.Api.Client.Models.Detail;
using AplosConnector.Common.Models;
using AplosConnector.Common.VendorCards;
using AplosConnector.Web.Models;

namespace AplosConnector.Web.Controllers
{
    [Route("api/[controller]")]
    public class AplosController : Controller
    {
        private readonly PexOAuthSessionStorage _pexOAuthSessionStorage;
        private readonly Pex2AplosMappingStorage _pex2AplosMappingStorage;
        private readonly IAplosIntegrationService _aplosIntegrationService;
        private readonly IVendorCardRepository _vendorCardRepository;

        public AplosController(
            PexOAuthSessionStorage pexOAuthSessionStorage,
            Pex2AplosMappingStorage pex2AplosMappingStorage,
            IAplosIntegrationService aplosIntegrationService,
            IVendorCardRepository vendorCardRepository)
        {
            _pex2AplosMappingStorage = pex2AplosMappingStorage;
            _pexOAuthSessionStorage = pexOAuthSessionStorage;
            _aplosIntegrationService = aplosIntegrationService;
            _vendorCardRepository = vendorCardRepository;
        }

        [HttpGet, Route("Accounts")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<PexAplosApiObject>>> GetAccounts(string sessionId, string category, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var accounts = await _aplosIntegrationService.GetAplosAccounts(mapping, category, cancellationToken);
            return Ok(accounts);
        }

        [HttpGet, Route("Account")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AplosApiAccountResponse>> GetAccount(string sessionId, decimal accountNumber, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var account = await _aplosIntegrationService.GetAplosAccount(mapping, accountNumber, cancellationToken);
            return Ok(account);
        }

        [HttpGet, Route("Contacts")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<PexAplosApiObject>>> GetContacts(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var contacts = await _aplosIntegrationService.GetAplosContacts(mapping, cancellationToken);
            return Ok(contacts);
        }

        [HttpGet, Route("Contact")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PexAplosApiObject>> GetContact(string sessionId, int aplosContactId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var contact = await _aplosIntegrationService.GetAplosContact(mapping, aplosContactId, cancellationToken);
            return Ok(contact);
        }

        [HttpGet, Route("Funds")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<PexAplosApiObject>>> GetFunds(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var funds = await _aplosIntegrationService.GetAplosFunds(mapping, cancellationToken);
            return Ok(funds);
        }

        [HttpGet, Route("Fund")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PexAplosApiObject>> GetFund(string sessionId, int aplosFundId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var fund = await _aplosIntegrationService.GetAplosFund(mapping, aplosFundId, cancellationToken);
            return Ok(fund);
        }

        [HttpGet, Route("TagCategories")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<PexAplosApiObject>>> GetTagCategories(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var tagCategories = await _aplosIntegrationService.GetAplosTagCategories(mapping, cancellationToken);

            return Ok(tagCategories);
        }

        [HttpGet, Route("TaxTagCategories")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<AplosApiTaxTagCategoryDetail>>> GetTaxTagCategories(string sessionId, CancellationToken cancellationToken)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancellationToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancellationToken);
            if (mapping == null) return NotFound();

            var taxTags = await _aplosIntegrationService.GetAplosApiTaxTagExpenseCategoryDetails(mapping, cancellationToken);
            return Ok(taxTags);
        }

        [HttpGet, Route("Vendors/ForCards")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<List<VendorForCardModel>>> GetVendorsForCards(string sessionId, [FromQuery] bool? activeOnly = true, [FromQuery] int? takeOnly = default, CancellationToken cancelToken = default)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return BadRequest();

            var session = await _pexOAuthSessionStorage.GetBySessionGuidAsync(sessionGuid, cancelToken);
            if (session == null) return Unauthorized();

            var mapping = await _pex2AplosMappingStorage.GetByBusinessAcctIdAsync(session.PEXBusinessAcctId, cancelToken);
            if (mapping == null) return NotFound();

            var contactsTask = _aplosIntegrationService.GetAplosContacts(mapping, cancelToken);
            var payablesTask = _aplosIntegrationService.GetAplosPayables(mapping, DateTime.Today.AddYears(-1), cancelToken);
            var vendorCardOrdersTask = _vendorCardRepository.GetAllVendorCardsOrderedAsync(mapping, cancelToken);

            await Task.WhenAll(contactsTask, payablesTask, vendorCardOrdersTask);

            var payablesResponse = payablesTask.Result ?? new AplosApiPayablesListResponse();
            var payables = payablesResponse.Data.Payables.Where(p => p.Contact.Type.Equals("company")).ToList();
            var vendorCards = vendorCardOrdersTask.Result?.SelectMany(x => x.CardOrders) ?? new List<VendorCardOrdered>();


            var vendorExpenses = from payable in payables
                group payable by payable.Contact.Id
                into g
                select new AplosVendorExpenseTotalModel
                {
                    Id = g.Key.ToString(),
                    DisplayName = g.First().Contact.CompanyName,
                    Total = g.Sum(s => s.Amount)
                };

            var results = from vendorExpense in vendorExpenses
                join vendorCard in vendorCards on vendorExpense.Name equals vendorCard.Name into vendorCardTemp
                from vendorCard in vendorCardTemp.DefaultIfEmpty() // left join
                where vendorCard == null
                orderby vendorExpense.Total descending
                select new VendorForCardModel
                {
                    Id = vendorExpense.Id,
                    Name = vendorExpense.Name,
                    Total = vendorExpense.Total,
                };

            return results.ToList();
        }
    }
}
