using System;
using System.Collections.Generic;
using System.Text;

namespace BuyTool_CLR
{
    public class RetailWeek : IEquatable<RetailWeek>, IComparable<RetailWeek>
    {
        public int Week { get; set; }
        public bool IsFullPriceWeek { get; set; }


        public bool Equals(RetailWeek other)
        {
            return this.Week == other.Week;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RetailWeek);
        }

        public int CompareTo(RetailWeek other)
        {
            return Week.CompareTo(other.Week);
        }

        public override int GetHashCode()
        {
            return Week.GetHashCode();
        }
    }
}
