using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace LambdaSharp.Challenge.Bookmarker.Shared {

    public class Bookmark {

        //--- Properties ---
        [JsonRequired]
        public string ID { get; set; }

        [JsonRequired]
        public Uri Url { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }
        public Uri ImageUrl { get; set; }
        public string Type { get; set; }
    }
}
