using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GirlfriendPanel.api.Models {
    public sealed class GfStatusRequest {
        public int Mood { get; set; }
        public int Hunger { get; set; }
        public int Energy { get; set; }
        public int Stress { get; set; }
        public string[]? Needs { get; set; }      
    }
}
