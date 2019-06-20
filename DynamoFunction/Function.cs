using System;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using OpenGraphNet;
using OpenGraphNet.Metadata;
using LambdaSharp.Challenge.Bookmarker.Shared;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaSharp.Challenge.Bookmarker.DynamoFunction {

    public class Function : ALambdaFunction<DynamoDBEvent, string> {

        //--- Fields ---
        private IAmazonDynamoDB _dynamoDbClient;
        private Table _table;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // initialize AWS clients
            _dynamoDbClient = new AmazonDynamoDBClient();

            // read settings
            var tableName = config.ReadDynamoDBTableName("BookmarksTable");
            _table = Table.LoadTable(_dynamoDbClient, tableName);
        }

        public override async Task<string> ProcessMessageAsync(DynamoDBEvent dynamoDbEvent) {
            LogInfo($"# DynamoDB Stream Records Count = {dynamoDbEvent.Records.Count}");
            foreach(var record in dynamoDbEvent.Records.Where(r => r.EventName == "INSERT")) {
                LogInfo($"EventID = {record.EventID}");
                var url = new Uri(record.Dynamodb.NewImage["Url"].S);
                try {
                    var graph = await OpenGraph.ParseUrlAsync(url);
                    var bookmark = new Bookmark {
                        ID = record.Dynamodb.NewImage["ID"].S,
                        Url = url,
                        Title = graph.Title,
                        Description = graph.Metadata["og:description"].FirstOrDefault()?.Value,
                        ImageUrl = graph.Image,
                        Type = graph.Type
                    };
                    LogInfo($"Updated Bookmark:\n{SerializeJson(bookmark)}");
                    _table.PutItemAsync(Document.FromJson(SerializeJson(bookmark))).Wait();
                } catch(Exception e) {
                    LogError(e);
                    continue;
                }
            }
            return "Ok";
        }

    }
}
