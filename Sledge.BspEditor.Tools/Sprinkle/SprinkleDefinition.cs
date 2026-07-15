using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Tools.Sprinkle
{
    public partial class SprinkleDefinition
    {
        public string Name { get; set; }
        public Dictionary<string, string> BaseProperties { get; set; }
        public List<SprinkleCase> Cases { get; set; }

        public SprinkleDefinition(SerialisedObject so)
        {
            Name = so.Name;
            Cases = new List<SprinkleCase>();
            BaseProperties = new Dictionary<string, string>();

            var baseObj = so.Children.FirstOrDefault(x => x.Name == "base");
            if (baseObj != null)
            {
                foreach (var prop in baseObj.Properties) BaseProperties[prop.Key] = prop.Value;
            }

            foreach (var child in so.Children.Where(x => x.Name != "base"))
            {
                if (int.TryParse(child.Name, out var chance))
                {
                    Cases.Add(new SprinkleCase { Chance = chance, Properties = child.Properties.ToDictionary(x => x.Key, x => x.Value) });
                }
            }
        }

        public Dictionary<string, string> GetRandomProperties(Random rand)
        {
            var totalChance = Cases.Sum(x => x.Chance);
            var roll = rand.Next(0, totalChance);
            var current = 0;
            foreach (var c in Cases)
            {
                current += c.Chance;
                if (roll < current) return c.Properties;
            }
            return Cases.First().Properties;
        }
    }

    public class SprinkleCase
    {
        public int Chance { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }
}