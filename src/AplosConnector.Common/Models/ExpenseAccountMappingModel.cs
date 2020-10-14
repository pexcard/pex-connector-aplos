using System;
using System.Collections.Generic;
using System.Text;

namespace AplosConnector.Common.Models
{
    public class ExpenseAccountMappingModel
    {
        //public int QuickBooksExpenseCategoryIdFilter { get; set; }
        public bool SyncExpenseAccounts { get; set; }
        public string ExpenseAccountsPexTagId { get; set; }
    }
}
