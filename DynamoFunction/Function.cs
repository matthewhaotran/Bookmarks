using System;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using OpenGraphNet;
using LambdaSharp.Challenge.Bookmarker.Shared;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaSharp.Challenge.Bookmarker.DynamoFunction {

    public class Function : ALambdaFunction<DynamoDBEvent, string> {

        //--- Fields ---
        private Table _table;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // read settings
            var tableName = config.ReadDynamoDBTableName("BookmarksTable");

            // initialize AWS clients
            var dynamoDbClient = new AmazonDynamoDBClient();
            _table = Table.LoadTable(dynamoDbClient, tableName);
        }

        public override async Task<string> ProcessMessageAsync(DynamoDBEvent dynamoDbEvent) {
            LogInfo($"# DynamoDB Stream Records Count = {dynamoDbEvent.Records.Count}");

            // process all newly inserted records
            foreach(var record in dynamoDbEvent.Records.Where(r => r.EventName == "INSERT")) {
                LogInfo($"EventID = {record.EventID}");

                // run asynchronous task to fetch metadata and store it
                RunTask(async () => {
                    try {
                        var url = new Uri(record.Dynamodb.NewImage["Url"].S);
                        var graph = await OpenGraph.ParseUrlAsync(url);
                        var bookmark = new Bookmark {
                            ID = record.Dynamodb.NewImage["ID"].S,
                            Url = url,
                            Title = graph.Title,
                            Description = graph.Metadata["og:description"].FirstOrDefault()?.Value,
                            ImageUrl = graph.Image,
                            Type = graph.Type
                        };
                        var serializedBookmark = SerializeJson(bookmark);
                        LogInfo($"Updated Bookmark:\n{serializedBookmark}");
                        await _table.PutItemAsync(Document.FromJson(serializedBookmark));
                    } catch(Exception e) {

                        // log error without letting it percolate; otherwise, the lambda function will
                        // not be able to process further DynamoDB records
                        LogError(e);
                    }
                });
            }
            return "Ok";
        }

    }
}
