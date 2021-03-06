open System
open System.IO
open Farmer
open Farmer.Builders
open Medallion.Shell

let resourceGroup = "dotnetru-rg"
let acrName = "dotnetruacr"
let logName = "dotnetru-log"
let appName = "rextester-app"

let servicePlanName = "dotnetru-service-plan"

let getEnv name =
    match Environment.GetEnvironmentVariable name with
    | null ->
        failwith $"Provide required ENV variable: {name}"
//        Console.WriteLine $"Please provide ENV variable: {name}"
//        Console.ReadLine()
    | value -> value
 
let appId = getEnv "DOTNETRU_DEPLOY_APPID"
let pwd = getEnv "DOTNETRU_DEPLOY_PWD"
let tenant = getEnv "DOTNETRU_DEPLOY_TENANT"
let apiKey = getEnv "REXTESTER_APIKEY"

type Result.ResultBuilder with
    member _.Bind(cmd: Command, next: unit -> Result<'a, string>): Result<'a, string> =
        cmd.StandardOutput.PipeToAsync(Console.Out, true)  |> ignore
        cmd.StandardError.PipeToAsync(Console.Error, true) |> ignore
        if cmd.Result.Success then
            next()
        else
            Error cmd.Result.StandardError

let logs = logAnalytics {
    name logName
    retention_period 30<Days>
    enable_query
    enable_ingestion
}

let registry = containerRegistry {
    name acrName
    
    sku ContainerRegistry.Basic
    enable_admin_user
}

let servicePlan = servicePlan {
    name servicePlanName
    sku WebApp.Sku.B1
    operating_system Linux
}

let proxyApp = webApp {
    name appName

    app_insights_off
    always_on
    
    link_to_service_plan servicePlan
    
    setting "REXTESTER_APIKEY" apiKey
    
    docker_ci
    docker_use_azure_registry acrName
    docker_image "rextester/proxy:latest" ""
}

let registryDeployment = arm {
    location Location.NorthEurope
    add_resource registry
    output "host" registry.LoginServer
    output "pwd" $"[listCredentials(resourceId('Microsoft.ContainerRegistry/registries','{acrName}'),'2017-10-01').passwords[0].value]"
    output "login" $"['{acrName}']"
}

//let servicePlanDeploy = arm {
//    location Location.NorthEurope
//    add_resource servicePlan
//}

let appDeployment = arm {
    location Location.NorthEurope
    add_resource servicePlan
    add_resource proxyApp
    add_resource logs
}

let addLogsToWebApp = arm {
    location Location.NorthEurope
    add_resource (Resource.ofJson $"""
{{
    "type": "Microsoft.Web/sites/providers/diagnosticSettings",
    "apiVersion": "2017-05-01-preview",
    "name": "[concat('{appName}', '/microsoft.insights/', '{logName}')]",
    "dependsOn": [],
    "properties": {{
        "workspaceId": "[resourceId('Microsoft.OperationalInsights/workspaces', '{logName}')]",
        "metrics": [],
        "logs": [
            {{
                "category": "AppServiceConsoleLogs",
                "enabled": true
            }},
            {{
                "category": "AppServiceAppLogs",
                "enabled": true
            }},
            {{
                "category": "AppServiceHTTPLogs",
                "enabled": true
            }}
        ]
    }}
}}
""")
}

let getAcrCreds() = result {
    let azShell = Shell(fun opts ->
        opts.ThrowOnError(false)
        |> ignore
        )
    do! azShell.Run("az", "login", "--service-principal", "-u", appId, "-p", pwd, "--tenant", tenant)
    let pwd = azShell.Run("az", "acr", "credential", "show", "--name", acrName, "--query", "passwords[0].value")
    let pwdStringRaw = pwd.Result.StandardOutput
    let pwdString = pwdStringRaw.Substring(1, pwdStringRaw.Length - 3) // truncating first and last chars
    return $"{acrName}.azurecr.io", acrName, pwdString
}

let pushDockerImage (host, user, pwd) = result {
    let dockerShell = Shell(fun opts ->
        opts.ThrowOnError(false)
            .WorkingDirectory("../")
        |> ignore
        )
    
    do! dockerShell.Run("docker", "login", "-u", user, "-p", pwd, host)
    do! dockerShell.Run("docker", "build", ".", "-t", "rextester-proxy")
    do! dockerShell.Run("docker", "tag", "rextester-proxy", $"{host}/rextester/proxy")
    do! dockerShell.Run("docker", "push", $"{host}/rextester/proxy")
    return ()
}

let deployAll() = result {
    // authenticate into Azure
    let! authResult = Deploy.authenticate appId pwd tenant
    printfn $"%A{authResult}"
    
    // deploying container registry
    let! registryDeploymentResult =
        Deploy.tryExecute
            resourceGroup
            Deploy.NoParameters
            registryDeployment
            
    printfn "ACR deployed successfully"
            
    let registryPwd = registryDeploymentResult.["pwd"]
    let registryLogin = registryDeploymentResult.["login"]
    let registryHost = registryDeploymentResult.["host"]

    // build&push image to registry
    do! pushDockerImage(registryHost, registryLogin, registryPwd)
    
    printfn "Docker image pushed successfully"
    
    // deploy webapp
    let! _ =
        Deploy.tryExecute
            resourceGroup
            [ proxyApp.DockerAcrCredentials.Value.Password.Value, registryPwd ]
            appDeployment
    printfn "Webapp, log analytic and service plan deployed successfully"
    
    // deploy logs
    let! _ =
        Deploy.tryExecute
            resourceGroup
            Deploy.NoParameters
            addLogsToWebApp
    return printfn "Logs linked with webapp successfully"
}

[<EntryPoint>]
let main argv =
    match argv with
    | null | [||] ->
        // deploy only image
        result {
            let! host, usr, pwd = getAcrCreds()
            do! pushDockerImage(host, usr, pwd)
        }
    | x when Array.contains "--all" x ->
        // deploy everything = resources + image
        deployAll()
    | x ->
        failwith $"Unknown arguments %A{x}"
    |> Result.get
    0
