# λ# Challenge - Bookmarker

This is a solution to the [λ# hackathon challenge](https://www.meetup.com/lambdasharp/) from June 19th, 2019.

## Overview
The challenge was to build a _Bookmarking API_ that allows saving, retrieving, sharing, and previewing of links.

### Infrastructure:
* [AWS DynamoDB](https://aws.amazon.com/dynamodb/) database for Bookmark data.
* [AWS API Gateway](https://aws.amazon.com/api-gateway/) implements Bookmark REST API
* [AWS Lambda](https://aws.amazon.com/lambda/) runs business logic invoked by API Gateway and DynamoDB Streams.
* [λ# Tool](https://lambdasharp.net/articles/ReleaseNotes-Favorinus.html) build and deploy CloudFormation stack and assets to AWS.

#### Bookmark Schema
```json
{
    "ID": "32a7cf5a-35be-459a-b586-ec2bed2f4fef",
    "Url": "https://www.youtube.com/watch?v=M5NVwuyk2uM",
    "Title": "The Dream Smartphone! (2019)",
    "Description": "The impossible dream of the perfect smartphone in ...",
    "ImageUrl": "https://i.ytimg.com/vi/M5NVwuyk2uM/maxresdefault.jpg"
}
```

## Setup - .NET Core and AWS
* [Install .Net 2.1](https://dotnet.microsoft.com/download/dotnet-core/2.1)
* [Sign up for AWS account](https://aws.amazon.com/)
* [Install AWS CLI](https://aws.amazon.com/cli/)

## Setup - λ# Tool (aka `lash`)

For this challenge, we will be using a pre-release version of the λ# Tool that is easier to configure and use.

If you have an earlier version of the λ# Tool installed, you will need to [uninstall it](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-uninstall) before upgrading. Make sure to specify the exact version when installing the λ# Tool.

```
dotnet tool install -g LambdaSharp.Tool --version 0.7-RC3
```

### Clone, Build, and Deploy Bookmarker

Begin by cloning the [Bookmarker GitHub repository](https://github.com/bjorg/Bookmarker).

```
git clone git@github.com:bjorg/Bookmarker.git
```

The hop into the folder to initialize λ# and deploy the Bookmarker module.

```bash
cd Bookmarker
lash init --quick-start     # one time
lash deploy                 # to propagate code changes
```

## Testing
Once the λ# module is deployed, you can test functionality of the REST API endpoints either using [Postman](https://www.getpostman.com/) or [cURL](https://curl.haxx.se/).

### Using Postman
* Import the [collection](./postman_collection.json) into Postman by clicking the `Import` button on the top left of the application.
* Add a collection variable `bookmark_api`, and set the value to the `ApiUrl` that was printed.
* Run the `New Bookmark` request.

### Using cURL
```
curl -X POST \
  https://{YOUR_API_GATEWAY_URL}/LATEST/bookmarks \
  -H 'Content-Type: application/json' \
  -d '{
    "Url": "https://www.youtube.com/watch?v=M5NVwuyk2uM"
}'
```

## Solution

### Shortening the URL
For the shortening of the bookmarked URL, we opted to compute the Base64-encoded SHA256 hash of the URL, and then use as few characters of it as needed to find a unique ID.

To avoid multiple round-trips to the DynamoDB table, we decided to fetch all candidate keys in a single batch read operation. With this approach, it was possible to find an existing mapping or the next available key in a single DynamoDB round-trip. A further optimization was to retrieve only the `ID` and `Url` of the fetched rows.

Note this implementation is missing a conditional row-insert operation to avoid a race condition where two different URLs could be shortened to the same available ID. This is left as an exercise to the reader.

### Fetching Open Graph Metadata
For the `DynamoFunction`, which fetches the [Open Graph metadata](http://ogp.me/), we used the  [OpenGraphNet](https://github.com/ghorsey/OpenGraph-Net) library. In addition, we took advantage of the [`ALambdaFunction.RunTask()`](https://lambdasharp.net/sdk/LambdaSharp.ALambdaFunction.html#LambdaSharp_ALambdaFunction_RunTask_System_Action_System_Threading_CancellationToken_) helper method to safely schedule multiple, concurrent operations in our Lambda function.

### Generating the Preview
For the preview, we had hoped to use the `OpenGraph.MakeGraph()` helper method, but it throws an exception for missing metadata properties and was therefore not suitable. Instead, we generated the output by hand using the `WebUtility.HtmlEncode()` where needed.

```html
<html>
    <head prefix="og:http://ogp.me/ns#">
        <title>The Dream Smartphone! (2019)</title>
        <meta property="og:title" content="The Dream Smartphone! (2019)">
        <meta property="og:type" content="video.other">
        <meta property="og:image" content="https://i.ytimg.com/vi/M5NVwuyk2uM/maxresdefault.jpg">
        <meta property="og:url" content="https://www.youtube.com/watch?v=M5NVwuyk2uM">
        <meta property="og:description" content="The impossible dream of the perfect smartphone in 2019! Special thanks to ConceptsCreator: http://youtube.com/Conceptcreator The Dream Smartphone 2014: https...">
        <meta property="og:site_name" content="www.youtube.com">
    </head>
    <body style="font-family: Helvetica, Arial, sans-serif;">
        <h1>The Dream Smartphone! (2019)</h1>
        <p>The impossible dream of the perfect smartphone in 2019! Special thanks to ConceptsCreator: http://youtube.com/Conceptcreator The Dream Smartphone 2014: https...</p>
        <img src="https://i.ytimg.com/vi/M5NVwuyk2uM/maxresdefault.jpg" />
    </body>
</html>
```

![sharing screenshot](./img/slack_screenshot.png)


### Generating the Redirect
For the redirect behavior, we use the `APIGatewayProxyResponse` return type to provide a response with a `HTTP 302 Found` status code and a `Location` header to redirect the browser to the original URL.

