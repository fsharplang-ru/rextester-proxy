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
open Microsoft.Extensions.Primitives

[<CLIMutable>]
type ToCompile =
    { LanguageChoice: int
      Program: string
      mutable ApiKey: string }

let apiKey = Environment.GetEnvironmentVariable "REXTESTER_APIKEY"
let apiUrl = Uri "https://rextester.com/rundotnet/api"
let allowedOrigin = "http://fsharplang.ru"

let createProxyRequest (ctx: HttpContext) = task {
    let req = ctx.Request
    let serializer = ctx.GetJsonSerializer()
    
    let proxyReq = new HttpRequestMessage()
    proxyReq.Method <- HttpMethod req.Method
    proxyReq.RequestUri <- apiUrl
    
    for KeyValue(name, value) in req.Headers do
        proxyReq.Headers.TryAddWithoutValidation(name, value)
        |> ignore
        
    proxyReq.Headers.Host <- apiUrl.Host
    
    let! toCompile = ctx.BindModelAsync<ToCompile>()
    toCompile.ApiKey <- apiKey
    let jsonContent = new StringContent(serializer.SerializeToString toCompile, Encoding.UTF8, "application/json");
    proxyReq.Content <- jsonContent
    
    return proxyReq
}

let setResponse (ctx: HttpContext) (response: HttpResponseMessage) = task {
    ctx.Response.StatusCode <- int response.StatusCode
    
    for KeyValue(name, value) in response.Headers do
        ctx.Response.Headers.[name] <- StringValues(Array.ofSeq value)
    
    for KeyValue(name, value) in response.Content.Headers do
        ctx.Response.Headers.[name] <- StringValues(Array.ofSeq value)
    
    ctx.Response.Headers.Remove "transfer-encoding" |> ignore
    
    do! response.Content.CopyToAsync ctx.Response.Body
}

let forwarder: HttpHandler = fun _ ctx -> task {
    let clientFactory = ctx.GetService<IHttpClientFactory>()
    let http = clientFactory.CreateClient()

    use! proxyReq = createProxyRequest ctx
    use! response = http.SendAsync(proxyReq, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted)

    do! setResponse ctx response
    
    return Some ctx
}

let configureServices (services : IServiceCollection) =
    services
        .AddCors()
        .AddRouting()
        .AddGiraffe()
        .AddHttpClient()
    |> ignore

let configureApp (appBuilder : IApplicationBuilder) =
    appBuilder
        .UseRouting()
        .UseCors(fun cors ->
            cors.WithOrigins(allowedOrigin)
                .AllowAnyHeader()
                .AllowAnyMethod()
            |> ignore
        )
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
