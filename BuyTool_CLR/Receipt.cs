using System;
using System.Collections.Generic;
using System.Text;

namespace BuyTool_CLR
{
    public class Receipt
    {
        public char ReceiptType { get; set;  }
        public char FlowType { get; set; }
        public int Week { get; set; }
        public int Qty { get; set; }
        public int CriticalWeek { get; set; }
        public decimal CriticalFraction { get; set; }
        public Dictionary<Tuple<int, int>, ReceiptComponents> NodeReceiptPlans { get; set; }

        public Receipt()
        {
            NodeReceiptPlans = new Dictionary<Tuple<int, int>, ReceiptComponents>();
        }


    }
}
