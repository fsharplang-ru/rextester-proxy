open System
open System.Net.Http
open System.Text
open Giraffe
open FSharp.Control.Tasks.NonAffine
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection

type ToCompile =
    { LanguageChoice: int
      Program: string
      mutable ApiKey: string }

let apiKey = Environment.GetEnvironmentVariable "REXTESTER_APIKEY"
let apiUrl = Uri "https://rextester.com/rundotnet/api"

let createProxyRequest (ctx: HttpContext) = task {
    let req = ctx.Request
    let serializer = ctx.GetJsonSerializer()
    
    let proxyReq = new HttpRequestMessage()
    proxyReq.Method <- HttpMethod req.Method
    proxyReq.RequestUri <- apiUrl
    
    for KeyValue(name, value) in req.Headers do
        if name <> "Host" then
            proxyReq.Headers.TryAddWithoutValidation(name, value)
            |> ignore
        
    let! toCompile = ctx.BindJsonAsync<ToCompile>()
    toCompile.ApiKey <- apiKey
    let jsonContent = new StringContent(serializer.SerializeToString toCompile, Encoding.UTF8, "application/json");
    proxyReq.Content <- jsonContent
    
    return proxyReq
}

let setResponse (ctx: HttpContext) (response: HttpResponseMessage) = task {
    ctx.Response.StatusCode <- int response.StatusCode
    
    for KeyValue(name, value) in response.Headers do
        ctx.SetHttpHeader(name, value)
    
    return! ctx.WriteStreamAsync(
                enableRangeProcessing = false,
                stream = response.Content.ReadAsStream(),
                eTag = None,
                lastModified = None
    )
}

let forwarder: HttpHandler = fun _ ctx -> task {
    let clientFactory = ctx.GetService<IHttpClientFactory>()
    let http = clientFactory.CreateClient()

    use! proxyReq = createProxyRequest ctx
    use! response = http.SendAsync proxyReq

    return! setResponse ctx response
}

let configureServices (services : IServiceCollection) =
    services
        .AddRouting()
        .AddGiraffe()
        .AddHttpClient()
    |> ignore

let configureApp (appBuilder : IApplicationBuilder) =
    appBuilder
        .UseRouting()
        .UseGiraffe forwarder

[<EntryPoint>]
let main args =
    WebHost
        .CreateDefaultBuilder(args)
        .UseKestrel()
        .UseUrls("http://*:5000")
        .Configure(configureApp)
        .ConfigureServices(configureServices)
        .Build()
        .Run()
    0
