using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using Microsoft.SqlServer.Server;
using System.Collections.Generic;
using BuyTool_CLR;
using System.Security.Principal;

public partial class StoredProcedures
{
    [Microsoft.SqlServer.Server.SqlProcedure]
    public static void Calc_Node_Receipt_Plans (SqlInt32 style_color_id, SqlBoolean verbose )
    {
        bool debug = false;
        SalesAndReceiptPlan salesAndReceiptPlan = new SalesAndReceiptPlan { MdStartWeekIndex = -1 };
        bool success = false;
        initializeReceipts(style_color_id, salesAndReceiptPlan);
        SqlPipe sqlP = SqlContext.Pipe;
        if (debug) sqlP.Send("Step = 1");
        if (salesAndReceiptPlan.FirstReceiptWeek > 0)
        {
            initializeBohAndPlanStart(style_color_id, salesAndReceiptPlan);

            if (debug) sqlP.Send("Step = 2");

            initializeNodeSalesPlans(style_color_id, salesAndReceiptPlan);
            if (debug) sqlP.Send("Step = 3");
            if (salesAndReceiptPlan.NodeSalesPlans != null)
            {
                if (debug) sqlP.Send("Step = 4");
                initializeItemParameters(style_color_id, salesAndReceiptPlan);
                if (debug) sqlP.Send("Step = 5");
                success = salesAndReceiptPlan.CalcNodeReceipts();

                if (success)
                {
                    if (debug) sqlP.Send("Step = 6");
                    writeOutput(style_color_id, salesAndReceiptPlan, verbose);
                }
            }
        }
        /*
        SqlPipe sqlP = SqlContext.Pipe;
        sqlP.Send("numWeeks = " + numWeeks);
        foreach (int week in weeks)
        {
            sqlP.Send("Week = " + week);
        }
        sqlP.Send("Grade ID, Climate ID, Store Count, Is Full Price Week, Sales Plan, Cum Sales Plan, Receipt Needs, Receipt Plan");

        foreach (NodePlan nodePlan in plans)
        {
            foreach (Plan plan in nodePlan.Plans)
            {
                sqlP.Send(nodePlan.GradeId + "," + nodePlan.ClimateId + "," + plan.StoreCount + "," + plan.IsFullPriceWeek
                     + "," + plan.SalesPlan + "," + plan.CumulativeSalesPlan + "," + plan.ReceiptNeed + "," + plan.ReceiptPlan);
            }
        }
        */
        //        sqlP.Send("Echo: GradeId =" + pl.GradeId + ", ClimateID = " + pl.ClimateId);
    }

    #region initializeNodeSalesPlans
    private static void initializeNodeSalesPlans(SqlInt32 style_color_id, SalesAndReceiptPlan salesAndReceiptPlan)
    {
        int numWeeks = 0;
        salesAndReceiptPlan.MdStartWeekIndex = -1;
        List<RetailWeek> weeksList = new List<RetailWeek>();
        List<NodeSalesPlan> pList = new List<NodeSalesPlan>();

        using (SqlConnection connection = new SqlConnection("context connection=true"))
        {
            using (SqlCommand cmd = new SqlCommand("[dbo].[Get_Node_Sales_Plan]", connection))
            {
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(
                    new SqlParameter
                    {
                        ParameterName = "@style_color_id",
                        SqlDbType = System.Data.SqlDbType.Int,
                        Direction = System.Data.ParameterDirection.Input,
                        Value = style_color_id,
                    });

                connection.Open();

                NodeSalesPlan previousNode = null;
                List<NodeWeekSalesPlan> planList = null;
                bool firstTime = true;
                decimal cumulativeSalesPlan = 0;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int gradeId = reader.GetInt32(0);
                        int climateId = reader.GetInt32(1);
                        if (previousNode == null || previousNode.GradeId != gradeId || previousNode.ClimateId != climateId)
                        {
                            if (previousNode != null)
                            {
                                int planLength = planList.Count;
                                if (planLength == numWeeks)
                                {
                                    previousNode.Plans = planList.ToArray();
                                }
                                else
                                {
                                    previousNode.Plans = new NodeWeekSalesPlan[numWeeks];
                                    planList.ToArray().CopyTo(previousNode.Plans, numWeeks - planLength);
                                }
                                pList.Add(previousNode);
                                firstTime = false;
                                cumulativeSalesPlan = 0;
                            }
                            planList = new List<NodeWeekSalesPlan>();
                            previousNode = new NodeSalesPlan() { GradeId = gradeId, ClimateId = climateId, Plans = null };
                        }

                        int week = reader.GetInt32(2);

                        bool isFullPriceWeek = reader.GetBoolean(3);
                        if (firstTime)
                        {
                            if (numWeeks == 0)
                            {
                                salesAndReceiptPlan.FirstSalesWeek = week;
                            }
                            if (salesAndReceiptPlan.MdStartWeekIndex < 0 && !isFullPriceWeek)
                            {
                                salesAndReceiptPlan.MdStartWeekIndex = numWeeks;
                            }
                            numWeeks++;
                            weeksList.Add(new RetailWeek { Week = week, IsFullPriceWeek = isFullPriceWeek } );
                        }

                        short storeCount = 0;
                        if (!reader.IsDBNull(4))
                        {
                            storeCount = reader.GetInt16(4);
                        }
                        decimal salesPlanU = 0;
                        if (!reader.IsDBNull(5))
                        {
                            cumulativeSalesPlan += salesPlanU = reader.GetDecimal(5);
                            if (isFullPriceWeek)
                            {
                                salesAndReceiptPlan.TotalFpSalesPlanU += salesPlanU;
                            }
                            else
                            {
                                salesAndReceiptPlan.TotalMdSalesPlanU += salesPlanU;
                            }
                        }
                        planList.Add(new NodeWeekSalesPlan { StoreCount = storeCount, IsFullPriceWeek = isFullPriceWeek, SalesPlanU = salesPlanU, CumulativeSalesPlanU = cumulativeSalesPlan});
                    }
                }
                if (previousNode != null)
                {
                    int planLength = planList.Count;
                    if (planLength == numWeeks)
                    {
                        previousNode.Plans = planList.ToArray();
                    }
                    else
                    {
                        previousNode.Plans = new NodeWeekSalesPlan[numWeeks];
                        planList.ToArray().CopyTo(previousNode.Plans, numWeeks - planLength);
                    }
                    pList.Add(previousNode);
                }
            }
        }
        if (numWeeks > 0)
        {
            salesAndReceiptPlan.Weeks = weeksList.ToArray();
            salesAndReceiptPlan.NodeSalesPlans = pList.ToArray();
            Dictionary<int, int> weekIndex = new Dictionary<int, int>();
            for (int i = 0; i < numWeeks; i++)
            {
                weekIndex.Add(salesAndReceiptPlan.Weeks[i].Week, i);
            }
            salesAndReceiptPlan.WeekIndex = weekIndex;
        }
        else
        {
            salesAndReceiptPlan.NodeSalesPlans = null;
        }
        salesAndReceiptPlan.NumWeeks = numWeeks;
        if (salesAndReceiptPlan.MdStartWeekIndex < 0)
        {
            salesAndReceiptPlan.MdStartWeekIndex = numWeeks;
        }
    }
    #endregion

    #region initializeItemParameters
    private static void initializeItemParameters(SqlInt32 style_color_id, SalesAndReceiptPlan salesAndReceiptPlan)
    {
        string query = "SELECT Weeks_Of_Cover, Pres_Min FROM AP_Style_Color WHERE Id = @style_color_id";
        salesAndReceiptPlan.WOC = 0;
        salesAndReceiptPlan.PresMin = 1;
        using (SqlConnection connection = new SqlConnection("context connection=true"))
        {
            using (SqlCommand cmd = new SqlCommand(query, connection))
            {
                cmd.CommandType = CommandType.Text;

                cmd.Parameters.Add(
                    new SqlParameter
                    {
                        ParameterName = "@style_color_id",
                        SqlDbType = System.Data.SqlDbType.Int,
                        Direction = System.Data.ParameterDirection.Input,
                        Value = style_color_id,
                    });

                connection.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        salesAndReceiptPlan.WOC = reader.IsDBNull(0) ? (byte)0 : reader.GetByte(0);
                        salesAndReceiptPlan.PresMin = reader.IsDBNull(1) ? (byte)1 : reader.GetByte(1);
                    }
                }
            }
        }
    }
    #endregion

    #region initializeReceipts
    private static void initializeReceipts(SqlInt32 style_color_id, SalesAndReceiptPlan salesAndReceiptPlan)
    {
        bool debug = false;
        int earliestReceiptWeek = -1;
        Dictionary<int, int> receiptPlan = new Dictionary<int, int>();
        Dictionary<int, int> purchaseOrders = new Dictionary<int, int>();
        Dictionary<int, int> overridesPlan = new Dictionary<int, int>();
        using (SqlConnection connection = new SqlConnection("context connection=true"))
        {
            using (SqlCommand cmd = new SqlCommand("dbo.Get_Receipts", connection))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                cmd.Parameters.Add(
                        new SqlParameter
                        {
                            ParameterName = "@style_color_id",
                            SqlDbType = System.Data.SqlDbType.Int,
                            Direction = System.Data.ParameterDirection.Input,
                            Value = style_color_id
                        });

                connection.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        int week = reader.GetInt32(0);
                        string receipt_type = reader.GetString(1);
                        int units = reader.GetInt32(2);
                        switch (receipt_type)
                        {
                            case "BuyTool":
                                receiptPlan.Add(week, units);
                                break;
                            case "PO":
                                purchaseOrders.Add(week, units);
                                break;
                            case "Override":
                                overridesPlan.Add(week, units);
                                break;
                            default:
                                throw new Exception("Unrecognized receipt_type from dbo.Get_Receipts - " + receipt_type);
                        }
                        if (week < earliestReceiptWeek || earliestReceiptWeek < 0)
                        {
                            earliestReceiptWeek = week;
                        }
                    }
                }
            }
        }
        SqlPipe sqlP = SqlContext.Pipe;

        salesAndReceiptPlan.FirstReceiptWeek = earliestReceiptWeek;

        int totalReceiptU = 0;
        int validReceiptsCount = 0;
        Dictionary<int, Receipt> receiptDict = new Dictionary<int, Receipt>();
        foreach (KeyValuePair<int, int> kvp in purchaseOrders)
        {
            receiptDict.Add(kvp.Key, new Receipt { Week = kvp.Key, Qty = kvp.Value, ReceiptType = 'P' });
            totalReceiptU += kvp.Value;
            validReceiptsCount++;
        }
        foreach (KeyValuePair<int, int> kvp in overridesPlan)
        {
            if (!receiptDict.ContainsKey(kvp.Key))
            {
                receiptDict.Add(kvp.Key, new Receipt { Week = kvp.Key, Qty = kvp.Value, ReceiptType = 'O' });
                totalReceiptU += kvp.Value;
                if (kvp.Value > 0)
                {
                    validReceiptsCount++;
                }
            }
        }
        foreach (KeyValuePair<int, int> kvp in receiptPlan)
        {
            if (!receiptDict.ContainsKey(kvp.Key))
            {
                receiptDict.Add(kvp.Key, new Receipt { Week = kvp.Key, Qty = kvp.Value, ReceiptType = 'T' });
                totalReceiptU += kvp.Value;
                validReceiptsCount++;
            }
        }
        if (debug)
        sqlP.Send("totalReceiptU " + totalReceiptU);
        salesAndReceiptPlan.TotalReceiptU = totalReceiptU;
        Receipt[] receipts = null;

        if (validReceiptsCount > 0)
        {

            receipts = new Receipt[validReceiptsCount];
            int i = 0;
            foreach (Receipt r in receiptDict.Values)
            {
                if (r.Qty > 0)
                {
                    receipts[i++] = r;
                }
                if (debug)
                    sqlP.Send(i.ToString());
            }
            Array.Sort<Receipt>(receipts, (x, y) => x.Week.CompareTo(y.Week));

            if (debug)
            {
                sqlP.Send("Receipt Type, Week");
                foreach (Receipt receipt in receipts)
                {
                    sqlP.Send(receipt.ReceiptType.ToString() + "," + receipt.Qty);
                }
            }
        }

        salesAndReceiptPlan.Receipts = receipts;
        if (debug)
            sqlP.Send("Completed GetReceipts");
    }
    #endregion

    #region initializeBohAndPlanStart
    private static void initializeBohAndPlanStart(SqlInt32 style_color_id, SalesAndReceiptPlan salesAndReceiptPlan)
    {
//        salesAndReceiptPlan.PlanStartWeek = -1;
        using (SqlConnection connection = new SqlConnection("context connection=true"))
        {
            using (SqlCommand cmd = new SqlCommand("dbo.Get_BOH_And_Plan_Start", connection))
            {
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                cmd.Parameters.AddRange(new SqlParameter[]
                    {
                            new SqlParameter
                            {
                                ParameterName = "@style_color_id",
                                SqlDbType = System.Data.SqlDbType.Int,
                                Direction = System.Data.ParameterDirection.Input,
                                Value = style_color_id
                            },
                            new SqlParameter
                            {
                                ParameterName = "@plan_start_week",
                                SqlDbType = System.Data.SqlDbType.Int,
                                Direction = System.Data.ParameterDirection.Output,
                            },
                            new SqlParameter
                            {
                                ParameterName = "@beginning_on_hand",
                                SqlDbType = System.Data.SqlDbType.Int,
                                Direction = System.Data.ParameterDirection.Output,
                            }
                    });

                connection.Open();
                cmd.ExecuteNonQuery();

//                salesAndReceiptPlan.PlanStartWeek = (int)cmd.Parameters["@plan_start_week"].Value;
                salesAndReceiptPlan.BOH = (int)cmd.Parameters["@beginning_on_hand"].Value;
            }
        }

//        salesAndReceiptPlan.InitializePlan();
    }
    #endregion

    #region writeOutput
    private static void writeOutput(SqlInt32 style_color_id, SalesAndReceiptPlan salesAndReceiptPlan, SqlBoolean verbose, bool debug = false)
    {
        string[] storedProcs = verbose.IsTrue ?
            new string[] { "Save_Consolidated_Receipts", "Save_Consolidated_Node_Receipts", "Save_Node_Receipt_Needs_Debug" } :
            new string[] { "Save_Consolidated_Receipts", "Save_Consolidated_Node_Receipts" };
        int numStoredProcs = verbose ? 3 : 2;
        SqlPipe sqlP = SqlContext.Pipe;
        for (int i = 0; i < numStoredProcs; i++)
        {
            using (SqlConnection connection = new SqlConnection("context connection=true"))
            {
                using (SqlCommand cmd = new SqlCommand(storedProcs[i], connection))
                {
                    cmd.CommandType = System.Data.CommandType.StoredProcedure;
                    if (debug)
                    sqlP.Send(storedProcs[i] + " started");

                    DataTable dt = null;
                    switch (i)
                    {
                        case 0:
                            if (debug)
                            {
                                sqlP.Send("Style_Color_Id, Receipt Type, Week");
                                foreach (Receipt receipt in salesAndReceiptPlan.Receipts)
                                {
                                    sqlP.Send(style_color_id.ToString() + "," + receipt.ReceiptType + "," + receipt.Week);
                                }
                            }
                            dt = salesAndReceiptPlan.GetConsolidatedReceiptsDataTable();
                            break;
                        case 1:
                            dt = salesAndReceiptPlan.GetConsolidatedNodeReceiptsDataTable();
                            break;
                        case 2:
                            dt = salesAndReceiptPlan.GetReceiptNeedsDebugDataTable();
                            break;
                    }
                    cmd.Parameters.AddRange(new SqlParameter[]
                        {
                            new SqlParameter
                            {
                                ParameterName = "@style_color_id",
                                SqlDbType = System.Data.SqlDbType.Int,
                                Direction = System.Data.ParameterDirection.Input,
                                Value = style_color_id
                            },
                            new SqlParameter
                            {
                                ParameterName = "@data",
                                SqlDbType = System.Data.SqlDbType.Structured,
                                TypeName = dt.TableName,
                                Direction = System.Data.ParameterDirection.Input,
                                Value = dt
                            }
                        });
                    connection.Open();
                    cmd.ExecuteNonQuery();

                    if (debug)
                        sqlP.Send(storedProcs[i] + " completed");
                }
            }
        }
        sqlP.Send(style_color_id.ToString() + " processed.");
    }
    #endregion



}
