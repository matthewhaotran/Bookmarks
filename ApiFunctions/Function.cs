using System;
using System.Net;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.Lambda.APIGatewayEvents;
using LambdaSharp.ApiGateway;
using LambdaSharp.Challenge.Bookmarker.Shared;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaSharp.Challenge.Bookmarker.ApiFunctions {

    public class Function : ALambdaApiGatewayFunction {

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

        public async Task<AddBookmarkResponse> AddBookmarkAsync(AddBookmarkRequest request) {
            LogInfo($"Add Bookmark: Url={request.Url}");

            // generate a url-safe SHA256 code from the url
            if(!Uri.TryCreate(request.Url, UriKind.Absolute, out var url)) {
                AbortBadRequest("Url not valid");
            }
            var keyCandidates = GenerateKeyCandidates(request.Url);

            // batch get documents with key candidates to see if they already exist
            var batch = _table.CreateBatchGet();
            keyCandidates.ForEach(keyCandidate => batch.AddKey(keyCandidate));
            batch.AttributesToGet = new List<string> {
                "ID",
                "Url"
            };
            await batch.ExecuteAsync();
            var foundBookmarks = batch.Results
                .Select(document => DeserializeJson<Bookmark>(document.ToJson()))
                .ToList();

            // check if url has already been shortened
            var existingDocument = foundBookmarks.FirstOrDefault(bookmark => bookmark.Url == url);
            if(existingDocument != null) {
                return  new AddBookmarkResponse {
                    ID = existingDocument.ID
                };
            }

            // find the first key that doesn't exist
            var newId = keyCandidates.First(keyCandidate => !foundBookmarks.Any(bookmark => bookmark.ID == keyCandidate));

            // store new bookmark and return the result
            await _table.PutItemAsync(Document.FromJson(SerializeJson(new Bookmark {
                ID = newId,
                Url = url
            })));
            return new AddBookmarkResponse {
                ID = newId
            };

            // local functions
            List<string> GenerateKeyCandidates(string uri) {
                using(var sha256 = SHA256.Create()) {
                    var hashValue = sha256.ComputeHash(Encoding.UTF8.GetBytes(uri));

                    // convert to base64 and remove url-unsafe characters
                    var encoded = Convert.ToBase64String(hashValue)
                        .Replace("/", "")
                        .Replace("+", "");
                    return Enumerable.Range(3, 12)
                        .Select(i => encoded.Substring(0,i))
                        .ToList();
                }
            }
        }

        public async Task<GetBookmarkResponse> GetBookmarkAsync(string id) {
            LogInfo($"Get Bookmark: ID={id}");
            var bookmark = (await RetrieveBookmarkAsync(id)) ?? throw AbortNotFound("Bookmark not found");
            return new GetBookmarkResponse {
                ID = bookmark.ID,
                Url = bookmark.Url,
                Title = bookmark.Title,
                Description = bookmark.Description,
                ImageUrl = bookmark.ImageUrl,
                Type = bookmark.Type
            };
        }

        public async Task<GetBookmarksResponse> GetBookmarksAsync() {
            return new GetBookmarksResponse {
                Bookmarks = await RetrieveAllBookmarksAsync()
            };
        }

        public async Task<DeleteBookmarkResponse> DeleteBookmarkAsync(string id) {
            LogInfo($"Delete Bookmark: ID={id}");
            var document = await _table.DeleteItemAsync(id);
            return new DeleteBookmarkResponse {
                Deleted = (document != null)
            };
        }

        public async Task<APIGatewayProxyResponse> GetBookmarkPreviewAsync(string id) {
            LogInfo($"Get Bookmark Preview: ID={id}");
            var bookmark = (await RetrieveBookmarkAsync(id)) ?? throw AbortNotFound("Bookmark not found");
            var html = new StringBuilder();
            html.Append($@"<html>");

            // create head with metadata
            html.Append($@"<head prefix=""og:http://ogp.me/ns#"">");
            html.Append($@"<title>{WebUtility.HtmlEncode(bookmark.Title)}</title>");
            AppendOpenGraphTag("title", bookmark.Title);
            AppendOpenGraphTag("type", bookmark.Type);
            AppendOpenGraphTag("image", bookmark.ImageUrl?.ToString());
            AppendOpenGraphTag("url", bookmark.Url.ToString());
            AppendOpenGraphTag("description", bookmark.Description);
            AppendOpenGraphTag("site_name", bookmark.Url.Host);
            html.Append($@"</head>");

            // create body
            html.Append($@"<body style=""font-family: Helvetica, Arial, sans-serif;"">");
            html.Append($@"<h1>{WebUtility.HtmlEncode(bookmark.Title)}</h1>");
            if(!string.IsNullOrEmpty(bookmark.Description)) {
                html.Append($@"<p>{bookmark.Description}</p>");
            }
            if(bookmark.ImageUrl != null) {
                html.Append($@"<img src=""{WebUtility.HtmlEncode(bookmark.ImageUrl.ToString())}"" />");
            }
            html.Append($@"</body>");
            html.Append($@"</html>");

            // send response
            return new APIGatewayProxyResponse {
                Body = html.ToString(),
                StatusCode = 200,
                Headers = new Dictionary<string,string> {
                    ["Content-Type"] = "text/html",
                },
            };

            // local functions
            void AppendOpenGraphTag(string tag, string value) {
                if(value != null) {
                    html.Append($@"<meta property=""og:{tag}"" content=""{WebUtility.HtmlEncode(value)}"">");
                }
            }
        }

        public async Task<APIGatewayProxyResponse> GetRedirectById(string id) {
            var bookmark = (await RetrieveBookmarkAsync(id)) ?? throw AbortNotFound("Bookmark not found");
            return new APIGatewayProxyResponse {
                StatusCode = 302,
                Headers = new Dictionary<string,string> {
                    ["Location"] = bookmark.Url.ToString()
                }
            };
        }

        public async Task<GetBookmarkTypesResponse> GetBookmarkTypes() {
            var bookmarks = await RetrieveAllBookmarksAsync();
            return new GetBookmarkTypesResponse {
                Types = bookmarks
                    .Select(bookmark => bookmark.Type)
                    .Distinct()
                    .ToArray()
            };
        }

        public async Task<GetBookmarksByTypeResponse> GetBookmarksByTypes(string type) {
            var bookmarks = await RetrieveAllBookmarksAsync();
            return new GetBookmarksByTypeResponse {
                Bookmarks = bookmarks
                    .Where(bookmark => bookmark.Type == type)
                    .ToList()
            };
        }

        private async Task<Bookmark> RetrieveBookmarkAsync(string id) {
            var document = await _table.GetItemAsync(id);
            return (document == null)
                ? null
                : DeserializeJson<Bookmark>(document.ToJson());
        }

        private async Task<List<Bookmark>> RetrieveAllBookmarksAsync() {
            var search = _table.Scan(new ScanFilter());
            var bookmarks = new List<Bookmark>();
            do {
                (await search.GetNextSetAsync()).ForEach(document
                    => bookmarks.Add(DeserializeJson<Bookmark>(document.ToJson()))
                );
            } while(!search.IsDone);
            return bookmarks;
        }
    }
}
