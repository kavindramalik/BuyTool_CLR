using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Data;

namespace BuyTool_CLR
{
    public class SalesAndReceiptPlan
    {
        public byte PresMin { get; set; }
        public byte WOC { get; set; }


        public NodeSalesPlan[] NodeSalesPlans { get; set; }
        public decimal TotalFpSalesPlanU { get; set; }
        public decimal TotalMdSalesPlanU { get; set; }
        public RetailWeek[] Weeks { get; set; }
        public int MdStartWeekIndex { get; set; }
        public Dictionary<int, int> WeekIndex { get; set; }
        public int FirstSalesWeek { get; set; }
        public int NumWeeks { get; set; }

        public decimal TotalReceiptU { get; set; }


        public int BOH { get; set; }
//        public int PlanStartWeek { get; set; }

        public int FirstReceiptWeek { get; set; }
//        public int SalesOffset { get; set; }

        public Receipt[] Receipts { get; set; }


        public SalesAndReceiptPlan() { }

        #region InitializeReceipts
        public void InitializeReceipts(Dictionary<int, int> receiptPlan, Dictionary<int, int> overridesPlan, Dictionary<int, int> purchaseOrders)
        {
            SqlPipe sqlP = SqlContext.Pipe;
            sqlP.Send("Step = 1");
            Receipts = null;
            TotalReceiptU = 0;
            HashSet<int> candidateReceiptWeeks = new HashSet<int>();
            if (receiptPlan.Count > 0)
            {
                candidateReceiptWeeks = new HashSet<int>(receiptPlan.Keys);
            }
            sqlP.Send("Step = 2");
            if (purchaseOrders.Count > 0)
            {
                if (candidateReceiptWeeks == null)
                {
                    candidateReceiptWeeks = new HashSet<int>(purchaseOrders.Keys);
                }
                else
                {
                    candidateReceiptWeeks.UnionWith(purchaseOrders.Keys);
                }
            }
            sqlP.Send("Step = 3");
            if (overridesPlan.Count > 0)
            {
                if (candidateReceiptWeeks == null)
                {
                    candidateReceiptWeeks = new HashSet<int>(overridesPlan.Keys);
                }
                else
                {
                    candidateReceiptWeeks.UnionWith(overridesPlan.Keys);
                }
            }
            sqlP.Send("Step = 4 - " + candidateReceiptWeeks.Count);

            if (candidateReceiptWeeks.Count > 0)
            {
                Receipts = new Receipt[candidateReceiptWeeks.Count];
                int index = 0;
                foreach (int week in candidateReceiptWeeks)
                {
                    int poQty = 0;
                    int overrideQty = 0;
                    int receiptPlanQty = 0;
                    char receiptType = 'P';
                    if (!purchaseOrders.TryGetValue(week, out poQty))
                    {
                        receiptType = 'O';
                        if (!overridesPlan.TryGetValue(week, out overrideQty))
                        {
                            receiptType = 'T';
                            receiptPlan.TryGetValue(week, out receiptPlanQty);
                        }
                    }
                    int qty = poQty + overrideQty + receiptPlanQty;

                    if (qty > 0)
                    {
                        Receipts[index++] = new Receipt { Week = week, Qty = qty, ReceiptType = receiptType };
                        TotalReceiptU += qty;
                    }
                }
            }
            if (Receipts.Length > 1)
            {
                Array.Sort<Receipt>(Receipts, (x,y) => x.Week.CompareTo(y.Week));
            }

        }

        #endregion

        #region CalcNodeReceipts
        public bool CalcNodeReceipts()
        {
            if (Receipts != null)
            {
                int receiptCount = Receipts.Length;
                if (receiptCount == 1 && FirstReceiptWeek < FirstSalesWeek)
                {
                    return calcSimpleReceiptPlan();
                }
                else
                {
                    return calcComplexReceiptPlan();
                }
            }
            else
            {
                return false;
            }
        }
        #endregion

        #region calcSimpleReceiptPlan
        private bool calcSimpleReceiptPlan()
        {
            Receipt receipt = Receipts[0];
            receipt.CriticalWeek = Weeks[NumWeeks -1].Week;
            receipt.CriticalFraction = 1;

            receipt.NodeReceiptPlans = new Dictionary<Tuple<int, int>, ReceiptComponents>();

            decimal totalSalesPlan = 0;
            foreach (NodeSalesPlan nodeSalesPlan in NodeSalesPlans)
            {
                totalSalesPlan += nodeSalesPlan.TotalNodeSales;
            }
            if (totalSalesPlan > 0)
            {
                foreach (NodeSalesPlan nodeSalesPlan in NodeSalesPlans)
                {
                    ReceiptComponents rc = new ReceiptComponents();
                    rc.Qty = receipt.Qty * nodeSalesPlan.TotalNodeSales / totalSalesPlan;
                    rc.StoreCount = nodeSalesPlan.Plans[NumWeeks - 1].StoreCount;
                    receipt.NodeReceiptPlans.Add(new Tuple<int, int>(nodeSalesPlan.GradeId, nodeSalesPlan.ClimateId), rc);
                }
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion

        #region calcComplexReceiptPlan
        private bool calcComplexReceiptPlan(bool debug = false)
        {
            SqlPipe sqlP = SqlContext.Pipe;
            int step = 0;
            if (debug)
                sqlP.Send(" calcComplexReceiptPlan Step " + step++);

            decimal[] cumulativeReceiptNeeds = CalcNodeReceiptNeeds();

            Dictionary<Tuple<int, int>, decimal> priorAllocation = new Dictionary<Tuple<int, int>, decimal>();
            if (debug)
                sqlP.Send(" calcComplexReceiptPlan Step " + step++);

            int earliestStartWeekIndex = -1;
            if (BOH == 0 && FirstSalesWeek < FirstReceiptWeek)
            {
                if (!WeekIndex.TryGetValue(FirstReceiptWeek, out earliestStartWeekIndex))
                {
                    return false;
                }
            }
            if (debug)
                sqlP.Send(" calcComplexReceiptPlan Step " + step++);
            // Assign BOH
            bool success = true;
            int criticalWeekIndex = 0;
            int qty = 0;
            if (BOH > 0)
            {
                qty = BOH;
                success &= allocate(qty, null, priorAllocation, earliestStartWeekIndex, cumulativeReceiptNeeds, ref criticalWeekIndex);

            }
            if (debug)
                sqlP.Send(" calcComplexReceiptPlan - BOH - Step " + step++);

            foreach (Receipt receipt in Receipts)
            {
                qty += receipt.Qty;
                success &= allocate(qty, receipt, priorAllocation, earliestStartWeekIndex, cumulativeReceiptNeeds, ref criticalWeekIndex);
                if (debug)
                    sqlP.Send(" calcComplexReceiptPlan - allocate receipt -  Step " + step++);

            }

            return success;
        }
        #endregion

        #region CalcNodeReceiptNeeds
        private decimal[] CalcNodeReceiptNeeds(bool debug = false)
        {
            SqlPipe sqlP = SqlContext.Pipe;

            int numWeeks = Weeks.Length;
            decimal[] cumulativeReceiptNeeds = new decimal[numWeeks];

            if (debug)
            sqlP.Send("Calc Receipt Needs - Num Weeks = " + numWeeks + ", Count of Node Sales Plans = " + NodeSalesPlans.Length);

            if (debug)
                sqlP.Send("Grade Id, Climate Id, Week, Store Count, i, endIndex, Prior Sales, Sales U, Cum Sales U, Target, Receipt Need, Cumulative Receipt Need, Remaining Receipt Need, Cumulative Receipt Needs");
            foreach (NodeSalesPlan nodeSalesPlan in NodeSalesPlans)
            {
                if (debug)
                {
                    int planLength = 0;
                    if (nodeSalesPlan.Plans != null)
                    {
                        planLength = nodeSalesPlan.Plans.Length;
                    }
                    sqlP.Send("Grade Id = " + nodeSalesPlan.GradeId + ", Climate Id = " + nodeSalesPlan.ClimateId + ", Plan Length = " + planLength );
                }
                bool traceStep = nodeSalesPlan.GradeId == 5 && nodeSalesPlan.ClimateId == 2 && debug;
                decimal cumulativePriorReceiptNeeds = 0;
                if (traceStep) sqlP.Send("Trace step = 0" );
                decimal remainingReceiptNeed = nodeSalesPlan.Plans[numWeeks - 1].CumulativeSalesPlanU;
                if (traceStep) sqlP.Send("Trace step = 1");
                for (int i = 0; i < MdStartWeekIndex; i++)
                {
                    if (traceStep) sqlP.Send("Trace i = " + i);
                    if (nodeSalesPlan.Plans[i] != null)
                    {
                        if (traceStep) sqlP.Send("Trace step = 2");
                        NodeWeekSalesPlan plan = nodeSalesPlan.Plans[i];

                        int endIndex = i + WOC; //This will need to be checked
                        if (endIndex >= numWeeks)
                        {
                            endIndex = numWeeks - 1;
                        }
                        if (traceStep) sqlP.Send("Trace step = 3");
                        if (!Weeks[endIndex].IsFullPriceWeek)
                        {
                            endIndex = numWeeks - 1;
                        }
                        if (traceStep) sqlP.Send("Trace step = 4");
                        decimal priorSales = i > 0 && nodeSalesPlan.Plans[i - 1] != null ? nodeSalesPlan.Plans[i - 1].CumulativeSalesPlanU : 0;
                        if (traceStep) sqlP.Send("Trace step = 5");
                        decimal target = i > 0 ?
                            PresMin * plan.StoreCount - priorSales + nodeSalesPlan.Plans[endIndex].CumulativeSalesPlanU :
                            PresMin * plan.StoreCount + nodeSalesPlan.Plans[endIndex].CumulativeSalesPlanU;
                        if (traceStep) sqlP.Send("Trace step = 6");
                        plan.ReceiptNeed = Math.Max(target - cumulativePriorReceiptNeeds + priorSales, 0);
                        if (plan.ReceiptNeed > remainingReceiptNeed)
                        {
                            plan.ReceiptNeed = remainingReceiptNeed;
                            remainingReceiptNeed = 0;
                        }
                        else
                        {
                            remainingReceiptNeed -= plan.ReceiptNeed;
                        }
                        cumulativePriorReceiptNeeds += plan.ReceiptNeed;
                        if (traceStep) sqlP.Send("Trace step = 7");
                        plan.CumulativeReceiptNeed = i > 0 && nodeSalesPlan.Plans[i - 1] != null ? nodeSalesPlan.Plans[i - 1].CumulativeReceiptNeed + plan.ReceiptNeed 
                            : plan.ReceiptNeed;
                        if (traceStep) sqlP.Send("Trace step = 8");
                        cumulativeReceiptNeeds[i] += plan.CumulativeReceiptNeed;
                        if (debug)
                            sqlP.Send(nodeSalesPlan.GradeId + "," + nodeSalesPlan.ClimateId + "," + Weeks[i].Week 
                                + "," + nodeSalesPlan.Plans[i].StoreCount
                            + "," + i + "," + endIndex + "," + priorSales + "," + nodeSalesPlan.Plans[i].SalesPlanU 
                            + "," + nodeSalesPlan.Plans[i].CumulativeSalesPlanU
                             + "," + target 
                             +"," + nodeSalesPlan.Plans[i].ReceiptNeed + "," + nodeSalesPlan.Plans[i].CumulativeReceiptNeed + "," +
                             remainingReceiptNeed + "," + cumulativeReceiptNeeds[i]);
                        if (endIndex == numWeeks - 1)
                        {
                            for (int k = i + 1; k < MdStartWeekIndex; k++)
                            {
                                nodeSalesPlan.Plans[k].CumulativeReceiptNeed = plan.CumulativeReceiptNeed;
                                cumulativeReceiptNeeds[k] += plan.CumulativeReceiptNeed;
                                if (debug)
                                    sqlP.Send(nodeSalesPlan.GradeId + "," + nodeSalesPlan.ClimateId + "," + Weeks[k].Week + "," + nodeSalesPlan.Plans[k].StoreCount
                                    + "," + k + "," + "" + "," + "" + "," + nodeSalesPlan.Plans[k].SalesPlanU + "," + nodeSalesPlan.Plans[k].CumulativeSalesPlanU
                                    + "," + "0" 
                                     + "," + nodeSalesPlan.Plans[k].ReceiptNeed + "," + nodeSalesPlan.Plans[k].CumulativeReceiptNeed + "," +
                                     remainingReceiptNeed + "," + cumulativeReceiptNeeds[k]);
                            }
                            break;
                        }
                    }
                }
            }
            if (debug)
            {
                sqlP.Send("Week, Cumulative Receipt Need");
                for (int i = 0; i < MdStartWeekIndex; i++)
                {
                    sqlP.Send(Weeks[i].Week +"," + cumulativeReceiptNeeds[i]);
                }
            }

            return cumulativeReceiptNeeds;
        }
        #endregion

        #region getCriticalWeekAndFraction
        private bool getCriticalWeekAndFraction(int earliestStartWeekIndex, int qty, decimal[] cumulativeReceiptNeeds, 
            ref int criticalWeekIndex, out decimal criticalFraction)
        {
            if (earliestStartWeekIndex > criticalWeekIndex)
            {
                criticalWeekIndex = earliestStartWeekIndex;
            }
            decimal ineligibleReceiptNeeds = earliestStartWeekIndex == -1 ? 0 : cumulativeReceiptNeeds[earliestStartWeekIndex];
            while (criticalWeekIndex < MdStartWeekIndex && cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds < qty)
            {
                if (criticalWeekIndex == MdStartWeekIndex - 1)
                    break;
                criticalWeekIndex++;
            }
            decimal priorWeekCumulativeReceiptNeeds = criticalWeekIndex > 0 ? cumulativeReceiptNeeds[criticalWeekIndex - 1] : 0;
            decimal currentWeekReceiptNeeds = cumulativeReceiptNeeds[criticalWeekIndex] - priorWeekCumulativeReceiptNeeds;
            criticalFraction = 1;
            if (qty > cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds)
            {
                if (cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds > 0)
                {
                    criticalFraction = qty / ( cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds);
                }
                else // no valid basis
                {
                    return false;
                }
            }
            else // most frequent -- qty <=  cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds
            {
                if ((qty == cumulativeReceiptNeeds[criticalWeekIndex] - ineligibleReceiptNeeds))
                {
                    criticalFraction = 1;
                }
                else // <
                {
                    criticalFraction = (qty - priorWeekCumulativeReceiptNeeds - ineligibleReceiptNeeds) / (cumulativeReceiptNeeds[criticalWeekIndex] - priorWeekCumulativeReceiptNeeds - ineligibleReceiptNeeds);
                }
            }
            return true;
        }
        #endregion

        #region allocate
        private bool allocate(int qty, Receipt receipt, Dictionary<Tuple<int, int>, decimal> priorAllocation,
            int earliestStartWeekIndex, decimal[] cumulativeReceiptNeeds, ref int criticalWeekIndex, bool debug = false)
        {
            SqlPipe sqlP = SqlContext.Pipe;
            int step = 0;
            if (debug)
                sqlP.Send(" allocate - -  Step " + step++);

            decimal criticalFraction;
            bool success = getCriticalWeekAndFraction(earliestStartWeekIndex, qty, cumulativeReceiptNeeds,
            ref criticalWeekIndex, out criticalFraction);
            if (debug)
                sqlP.Send(" allocate - -  Step " + step++);
            if (success)
            {
                if (receipt != null)
                {
                    receipt.CriticalWeek = Weeks[criticalWeekIndex].Week;
                    receipt.CriticalFraction = criticalFraction;
                }
                decimal totalQtyAllocated = 0;
                foreach (NodeSalesPlan nodeSalesPlan in NodeSalesPlans)
                {
                    decimal allocation = nodeSalesPlan.Allocation(earliestStartWeekIndex, criticalWeekIndex, criticalFraction);
                    if (allocation > 0)
                    {
                        Tuple<int, int> tuple = new Tuple<int, int>(nodeSalesPlan.GradeId, nodeSalesPlan.ClimateId);
                        decimal priorAllocatedQty;
                        if (!priorAllocation.TryGetValue(tuple, out priorAllocatedQty))
                        {
                            priorAllocatedQty = 0;
                        }
                        if (allocation > priorAllocatedQty)
                        {
                            if (receipt != null)
                            {
                                ReceiptComponents rc = new ReceiptComponents();
                                rc.Qty = allocation - priorAllocatedQty;
                                rc.StoreCount = nodeSalesPlan.Plans[criticalWeekIndex].StoreCount;
                                receipt.NodeReceiptPlans.Add(tuple, rc);
                            }
                            priorAllocation[tuple] = allocation;
                            totalQtyAllocated += allocation;
                        }
                    }
                }
                if (Math.Abs(totalQtyAllocated - qty) > (decimal)0.01)
                {
                    if (debug)
                     sqlP.Send("Target Allocation Qty = " + qty + ", Allocated Qty = " + totalQtyAllocated);
                }
            }
            return success;
        }
        #endregion

        #region GetConsolidatedReceiptsDataTable
        public DataTable GetConsolidatedReceiptsDataTable(bool debug = false)
        {
            SqlPipe sqlP = SqlContext.Pipe;

            DataTable dt = new DataTable();
            dt.TableName = "[dbo].[t_Consolidated_Receipts]";

            DataColumn col = new DataColumn("Receipt_Type", typeof(char));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Week", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Receipt_U", typeof(int));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            col = new DataColumn("Critical_Week", typeof(int));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            col = new DataColumn("Critical_Fraction", typeof(decimal));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            if (debug)
            sqlP.Send("Table - Receipt Type, Week, Receipt_U, Critical_Week, Critical_Fraction");
            foreach (Receipt receipt in Receipts)
            {
                DataRow row = dt.NewRow();

                row[0] = receipt.ReceiptType;
                row[1] = receipt.Week;
                row[2] = receipt.Qty;
                row[3] = receipt.CriticalWeek;
                row[4] = Math.Round(receipt.CriticalFraction, 5);
                if (debug)
                    sqlP.Send(receipt.ReceiptType + "," + receipt.Week + "," + receipt.Qty + "," + receipt.CriticalWeek + "," + receipt.CriticalFraction);

                dt.Rows.Add(row);
            }

            return dt;
        }
        #endregion

        #region GetConsolidatedNodeReceiptsDataTable
        public DataTable GetConsolidatedNodeReceiptsDataTable()
        {
            DataTable dt = new DataTable();
            dt.TableName = "[dbo].[t_Consolidated_Node_Receipts]";

            DataColumn col = new DataColumn("Receipt_Type", typeof(char));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Grade_Id", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Climate_Id", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Week", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Store_Count", typeof(decimal));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            col = new DataColumn("Node_Receipt_U", typeof(decimal));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            foreach (Receipt receipt in Receipts)
            {
                if (receipt.NodeReceiptPlans != null)
                {
                    foreach (KeyValuePair<Tuple<int, int>, ReceiptComponents> kvp in receipt.NodeReceiptPlans)
                    {
                        DataRow row = dt.NewRow();
                        row[0] = receipt.ReceiptType;
                        row[1] = kvp.Key.Item1;
                        row[2] = kvp.Key.Item2;
                        row[3] = receipt.Week;
                        row[4] = Math.Round(kvp.Value.StoreCount, 3);
                        row[5] = Math.Round(kvp.Value.Qty, 3);

                        dt.Rows.Add(row);
                    }
                }
            }

            return dt;
        }
        #endregion

        #region GetReceiptNeedsDebugDataTable
        public DataTable GetReceiptNeedsDebugDataTable(bool debug = false)
        {
            DataTable dt = new DataTable();
            dt.TableName = "[dbo].[t_Node_Receipt_Needs_Debug]";

            DataColumn col = new DataColumn("Grade_Id", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Climate_Id", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Week", typeof(int));
            col.AllowDBNull = false;
            dt.Columns.Add(col);

            col = new DataColumn("Receipt_Need_U", typeof(decimal));
            col.AllowDBNull = true;
            dt.Columns.Add(col);

            SqlPipe sqlP = SqlContext.Pipe;
            if (debug)
                sqlP.Send("MdStart Week = " + MdStartWeekIndex);
            foreach (NodeSalesPlan nodeSalesPlan in NodeSalesPlans)
            {
                if (nodeSalesPlan != null)
                {
                    for (int i = 0; i < MdStartWeekIndex; i++)
                    {
                        if (nodeSalesPlan.Plans[i] != null)
                        {
                            DataRow row = dt.NewRow();
                            row[0] = nodeSalesPlan.GradeId;
                            row[1] = nodeSalesPlan.ClimateId;
                            row[2] = Weeks[i].Week;
                            row[3] = Math.Round(nodeSalesPlan.Plans[i].ReceiptNeed, 3);
                            if (debug)
                                sqlP.Send(nodeSalesPlan.GradeId + "," + nodeSalesPlan.ClimateId + "," + Weeks[i].Week + "," + nodeSalesPlan.Plans[i].ReceiptNeed);

                            dt.Rows.Add(row);
                        }
                    }
                }
            }

            return dt;
        }
        #endregion


        /*
         * 
        
        #region InitializePlan
        public void InitializePlan()
        {
            if (PlanStartWeek < 0)
            {
                PlanStartWeek = FirstReceiptWeek;
            }
            if (PlanStartWeek > FirstSalesWeek)
            {
                PlanStartWeek = FirstSalesWeek;
            }
        }
        #endregion

        #region UpdateSalesPlan
        public void UpdateSalesPlan()
        {
            TotalScaledFpSalesPlanU = 0;
            TotalScaledMdSalesPlanU = 0;

            decimal dcomFpSalesU = 0;
            decimal storeFpSalesU = 0;
            decimal dcomFpStoreWeeks = 0;
            int storeFpStoreWeeks = 0;

            int salesWeeksCount = Weeks.Length;
            if (TotalReceiptU + StartingBOP > TotalOriginalFpSalesPlanU)
            {
                decimal scalingFactor = TotalOriginalMdSalesPlanU > 0 ? (TotalReceiptU + StartingBOP - TotalOriginalFpSalesPlanU) / TotalOriginalMdSalesPlanU : 0;

                for (int i = 0; i < salesWeeksCount; i++)
                {
                    foreach (NodeSalesPlan p in NodeSalesPlans)
                    {
                        if (i < MdStartWeekIndex)
                        {
                            TotalScaledFpSalesPlanU += p.Plans[i].SalesPlanU = p.Plans[i].OriginalSalesPlanU;
                            p.Plans[i].SalesPlanR = p.Plans[i].OriginalSalesPlanR;

                            if (p.Plans[i].SalesPlanU > 0)
                            {
                                if (p.ClimateId == -1)
                                {
                                    dcomFpSalesU += p.Plans[i].SalesPlanU;
                                    dcomFpStoreWeeks++;
                                }
                                else
                                {
                                    storeFpSalesU += p.Plans[i].SalesPlanU;
                                    storeFpStoreWeeks += p.Plans[i].StoreCount;
                                }
                            }
                        }
                        else
                        {
                            TotalScaledMdSalesPlanU += p.Plans[i].SalesPlanU = p.Plans[i].OriginalSalesPlanU * scalingFactor;
                            p.Plans[i].SalesPlanR = p.Plans[i].OriginalSalesPlanR * scalingFactor;
                        }
                    }
                }
            }
            else
            {
                for (int i = MdStartWeekIndex; i < salesWeeksCount; i++)
                {
                    foreach (NodeSalesPlan p in NodeSalesPlans)
                    {
                        p.Plans[i].SalesPlanU = 0;
                        p.Plans[i].SalesPlanR = 0;
                    }
                }
                if (TotalReceiptU + StartingBOP < TotalOriginalFpSalesPlanU)
                {
                    decimal scalingFactor = TotalOriginalFpSalesPlanU > 0 ? (TotalReceiptU + StartingBOP) / TotalOriginalFpSalesPlanU : 0;
                    for (int i = 0; i < MdStartWeekIndex; i++)
                    {
                        foreach (NodeSalesPlan p in NodeSalesPlans)
                        {
                            TotalScaledFpSalesPlanU += p.Plans[i].SalesPlanU = p.Plans[i].OriginalSalesPlanU * scalingFactor;
                            p.Plans[i].SalesPlanR = p.Plans[i].OriginalSalesPlanR * scalingFactor;

                            if (p.Plans[i].SalesPlanU > 0)
                            {
                                if (p.ClimateId == -1)
                                {
                                    dcomFpSalesU += p.Plans[i].SalesPlanU;
                                    dcomFpStoreWeeks++;
                                }
                                else
                                {
                                    storeFpSalesU += p.Plans[i].SalesPlanU;
                                    storeFpStoreWeeks += p.Plans[i].StoreCount;
                                }
                            }

                        }
                    }
                }
                else // ==
                {
                    for (int i = 0; i < MdStartWeekIndex; i++)
                    {
                        foreach (NodeSalesPlan p in NodeSalesPlans)
                        {
                            TotalScaledFpSalesPlanU += p.Plans[i].SalesPlanU = p.Plans[i].OriginalSalesPlanU;
                            p.Plans[i].SalesPlanR = p.Plans[i].OriginalSalesPlanR;
                            if (p.Plans[i].SalesPlanU > 0)
                            {
                                if (p.ClimateId == -1)
                                {
                                    dcomFpSalesU += p.Plans[i].SalesPlanU;
                                    dcomFpStoreWeeks++;
                                }
                                else
                                {
                                    storeFpSalesU += p.Plans[i].SalesPlanU;
                                    storeFpStoreWeeks += p.Plans[i].StoreCount;
                                }
                            }
                        }
                    }
                }
            }
            DcomAps = dcomFpStoreWeeks > 0 ? dcomFpSalesU / dcomFpStoreWeeks : 0;
            FinalAps = storeFpStoreWeeks > 0 ? storeFpSalesU / storeFpStoreWeeks : 0;
        }
        #endregion

        */


    }
}
