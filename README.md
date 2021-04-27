#Description
This is Rextester proxy for https://github.com/fsharplang-ru/fsharplang-ru.github.io
It should add ApiKey to body, forward request and return response

#Deploy
It deploys to Azure via Farmer.

Go to the `farmer` folder and run it

There are two scenarios
- Deploy Azure resources together with docker image
- Build and push docker image only into predeployed container registry

To deploy everything pass `--all` argument

To deploy just an image don't pass any argument

#Prerequisites
- AZ CLI
- Docker
- Net5 SDK 

- Azure credentials (put them in env variables)
    - `REXTESTER_DEPLOY_APPID`
    - `REXTESTER_DEPLOY_PWD`
    - `REXTESTER_DEPLOY_TENANT`

- Proxy settings
    - `REXTESTER_APIKEY` variable

#Run
`dotnet run` (from project folder)

or just run from IDE