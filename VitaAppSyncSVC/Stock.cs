using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VitaAppSyncSVC
{
    public class Stocks
    {
        public Stock[] list { get; set; }
    }

    public class Stock
    {
        public string item_uuid { get; set; }
        public string store_uuid { get; set; }
        public int available_count { get; set; }
        public float price { get; set; }
        public float price_crossed { get; set; }
    }

    public static class ListExtentions
    {
        public static List<List<T>> Split<T>(this List<T> source, int count)
        {
            return source.Select((x, y) => new { Index = y, Value = x }).GroupBy(x => x.Index / count).Select(x => x.Select(y => y.Value).ToList()).ToList();
        }
    }

    public class Regions
    {
        public Region[] list { get; set; }
        public int total { get; set; }
    }

    public class Region
    {
        public string uuid { get; set; }
        public string name { get; set; }
        public bool is_deleted { get; set; }
        public int timezone { get; set; }
    }

}
