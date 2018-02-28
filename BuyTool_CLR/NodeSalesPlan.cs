using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Text;
using System.Threading.Tasks;

namespace BuyTool_CLR
{
    public class NodeSalesPlan
    {
        public int GradeId { get; set; }
        public int ClimateId { get; set; }

        public NodeWeekSalesPlan[] Plans { get; set; }

        public NodeSalesPlan()
        {
        }

        public decimal Allocation(int earliestStartWeekIndex, int criticalWeekIndex, decimal criticalFraction)
        {
            decimal integralMeasure = criticalWeekIndex > 0 && Plans[criticalWeekIndex - 1] != null ? Plans[criticalWeekIndex - 1].CumulativeReceiptNeed : 0;
            if (earliestStartWeekIndex >= 0)
            {
                integralMeasure -= Plans[earliestStartWeekIndex].CumulativeReceiptNeed;
            }
            if (criticalFraction == 1)
            {
                return Plans[criticalWeekIndex] != null ? integralMeasure + Plans[criticalWeekIndex].ReceiptNeed : integralMeasure;
            }
            if (criticalFraction < 1)
            {
                decimal fractionalMeasure = Plans[criticalWeekIndex] != null ? Plans[criticalWeekIndex].ReceiptNeed : 0;
                return integralMeasure + criticalFraction * fractionalMeasure;
            }
            // criticalFraction > 1
            return (Plans[criticalWeekIndex] != null ? integralMeasure + Plans[criticalWeekIndex].ReceiptNeed : integralMeasure) * criticalFraction;

        }


        public decimal TotalNodeSales
        {
            get
            {
                if (Plans != null)
                {
                    return Plans[Plans.Length - 1].CumulativeSalesPlanU;
                }
                return 0;
            }
        }
    }
}

