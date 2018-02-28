using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Text;

namespace BuyTool_CLR
{
    public class NodeWeekSalesPlan
    {
        public short StoreCount { get; set; }
        public bool IsFullPriceWeek { get; set; }
        public decimal SalesPlanU { get; set; }
        public decimal CumulativeSalesPlanU { get; set; }
        public decimal ReceiptNeed { get; set; }
        public decimal CumulativeReceiptNeed { get; set; }


    }
}
