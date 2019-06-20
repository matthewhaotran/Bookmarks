using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using OpenGraphNet;
using LambdaSharp.ApiGateway;
using LambdaSharp.Challenge.Bookmarker.Shared;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaSharp.Challenge.Bookmarker.ApiFunctions {

    public class Function : ALambdaApiGatewayFunction {

        //--- Fields ---
        private IAmazonDynamoDB _dynamoDbClient;
        private Table _table;

        public override async Task InitializeAsync(LambdaConfig config) {

            // initialize AWS clients
            _dynamoDbClient = new AmazonDynamoDBClient();

            // read settings
            var tableName = config.ReadDynamoDBTableName("BookmarksTable");
            _table = Table.LoadTable(_dynamoDbClient, tableName);
        }

        public AddBookmarkResponse AddBookmark(AddBookmarkRequest request) {
            LogInfo($"Add Bookmark:  Url={request.Url}");
            Uri url;
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out url)) AbortBadRequest("Url Not Valid");

            // Level 1: generate a short ID that is still unique
            var id = "1";
            SHA256 mySHA256 = SHA256.Create();
            byte[] hashValue = mySHA256.ComputeHash(Encoding.ASCII.GetBytes(request.Url));
            var encoded = System.Convert.ToBase64String(hashValue).Replace('/', '_').Replace('+', '-');
            for (int i = 3; i < 30; i++) {
                var temporaryId = encoded.Substring(0,i);
                var temporaryBookmark = RetrieveBookmark(temporaryId);
                if(temporaryBookmark == null) {
                    id = temporaryId;
                    break;
                } else if(temporaryBookmark.Url.ToString() == request.Url) {
                    id = temporaryId;
                    break;
                }
            }
            var bookmark = new Bookmark {
                ID = id,
                Url = url,
            };
            _table.PutItemAsync(Document.FromJson(SerializeJson(bookmark))).Wait();
            return new AddBookmarkResponse{
                ID = bookmark.ID
            };
         }
        public GetBookmarkResponse GetBookmark(string id) {
            LogInfo($"Get Bookmark: ID={id}");
            var bookmark = RetrieveBookmark(id) ?? throw AbortNotFound("Bookmark not found");
            return new GetBookmarkResponse{
                ID = bookmark.ID,
                Url = bookmark.Url,
                Title = bookmark.Title,
                Description = bookmark.Description,
                ImageUrl = bookmark.ImageUrl,
            };
        }

        public GetBookmarksResponse GetBookmarks(string contains = null, int offset = 0, int limit = 10) {
            var search = _table.Scan(new ScanFilter());
            var bookmarks = new List<Bookmark>();
            do {
                var documentList = search.GetNextSetAsync().Result;
                foreach (var document in documentList)
                    bookmarks.Add(DeserializeJson<Bookmark>(document.ToJson()));
            } while (!search.IsDone);
            return new GetBookmarksResponse{
                Bookmarks = bookmarks
            };
        }

        public DeleteBookmarkResponse DeleteBookmark(string id) {
            LogInfo($"Delete Bookmark: ID={id}");
            _table.DeleteItemAsync(id).Wait();
            return new DeleteBookmarkResponse{
                Deleted = true,
            };
        }

        public APIGatewayProxyResponse GetBookmarkPreview(string id) {
            LogInfo($"Get Bookmark Preview: ID={id}");
            var bookmark = RetrieveBookmark(id) ?? throw AbortNotFound("Bookmark not found");
            var html = new StringBuilder();
            html.Append($@"<html>");

            // create head
            html.Append($@"<head>");
            html.Append($@"<title>{WebUtility.HtmlEncode(bookmark.Title)}</title>");
            html.Append($@"<meta property=""og:title"" content=""{WebUtility.HtmlEncode(bookmark.Title)}"" />");
            if(!string.IsNullOrEmpty(bookmark.Description)) {
                html.Append($@"<meta property=""og:description"" content=""{WebUtility.HtmlEncode(bookmark.Description)}"" />");
            }
            if(bookmark.ImageUrl != null) {
                html.Append($@"<meta property=""og:image"" content=""{WebUtility.HtmlEncode(bookmark.ImageUrl.ToString())}"" />");
            }
            if(bookmark.Type != null) {
                html.Append($@"<meta property=""og:type"" content=""{WebUtility.HtmlEncode(bookmark.Type)}"" />");
            }
            html.Append($@"</head>");

            // create body
            html.Append($@"<body style=""font-family: Helvetica, Arial, sans-serif;"">");
            html.Append($@"<h1>{WebUtility.HtmlEncode(bookmark.Title)}</h1>");
            if(!string.IsNullOrEmpty(bookmark.Description)) {
                html.Append($@"<p>{WebUtility.HtmlEncode(bookmark.Description)}</p>");
            }
            if(bookmark.ImageUrl != null) {
                html.Append($@"<img src=""{WebUtility.HtmlEncode(bookmark.ImageUrl.ToString())}"" />");
            }
            html.Append($@"</body>");
            html.Append($@"</html>");

            // send response
            return new APIGatewayProxyResponse{
                Body = html.ToString(),
                StatusCode = 200,
                Headers = new Dictionary<string,string>(){
                    ["Content-Type"] = "text/html",
                },
            };
        }

        public APIGatewayProxyResponse GetRedirectById (string id) {
            var bookmark = RetrieveBookmark(id) ?? throw AbortNotFound("Bookmark not found");
            return new APIGatewayProxyResponse{
                StatusCode = 302,
                Headers = new Dictionary<string,string>(){
                    ["Location"] = bookmark.Url.ToString()
                }
            };
        }
        
        public GetBookmarkTypesResponse GetBookmarkTypes() {
            var records = _table.Scan(new ScanFilter());
            return new GetBookmarkTypesResponse {
                Types = records.Matches
                    .Select(doc => DeserializeJson<Bookmark>(doc.ToJson()))
                    .Select(bookmark => bookmark.Type)
                    .Distinct()
                    .ToArray()
            };
        }

        public GetBookmarksByTypeResponse GetBookmarksByTypes(string type) {
            var records = _table.Scan(new ScanFilter());
            return new GetBookmarksByTypeResponse {
                Bookmarks = records.Matches
                    .Select(doc => DeserializeJson<Bookmark>(doc.ToJson()))
                    .Where(bookmark => bookmark.Type == type)
                    .ToList()
            };
        }

        private Bookmark RetrieveBookmark(string id) {
            var document = _table.GetItemAsync(id).Result;
            return (document == null)
                ? null
                : DeserializeJson<Bookmark>(document.ToJson());
        }
    }
}
