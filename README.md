## EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB
MongoDB web proxy implementation for the [EnfusionDatabaseFramework](https://github.com/Arkensor/EnfusionDatabaseFramework).

## ⚡ Quickstart
1. Install [`Docker`](https://www.docker.com) on your dedicated server or `Docker Desktop` for your local development environment.
2. Create a folder where you want to install and host the proxy application and MongoDB server. For example `C:/ArmaReforger/EDF-MongoDB`
3. Create a file named `docker-compose.yml` in that folder with the following content:
```yml
version: '3'
services:
  proxy-app:
    image: 'arkensor/enfusiondatabaseframework-proxy-mongodb:latest'
    restart: unless-stopped
    ports:
      - '8008:8008'
    environment:
      DBHOST: "mongodb"

  mongodb:
    image: 'mongodb/mongodb-community-server:latest'
    restart: unless-stopped
    ports:
      - '27017:27017'
    volumes:
      - ./data/mongodb:/data/db
```
4. Open a command line in the folder and type `docker compose up -d`
5. If everything works you can now set up your [connection info](https://github.com/Arkensor/EnfusionDatabaseFramework/blob/armareforger/docs/drivers/mongodb.md) in the Arma Reforger Workbench and use a tool like [MongoDB Compass](https://www.mongodb.com/try/download/compass) to connect to the MongoDB and view your stored data.

### Troubleshooting
If MongoDB refuses to start/is unreachable check that the folder permissions are set correctly.  
On Linux you might need to execute this command in the folder you created: `chmod -R a+rwx data/`

## 🛠️ Running it without Docker
If for some reason you prefer not to use Docker then right now there are no pre-compiled binaries available. You can however clone this project, open it in Visual Studio and publish it for whatever native runtime you need.
There are no magic setup steps required. Just Visual Studio with the .NET desktop development preset installed. The project uses .NET 7.0 but should generally be compatible with other versions.

## 📖 Options
Options can be passed via the environment variables or startup parameters with `--OPTIONNAME=VALUE`
- `DbHost` Hostname of the MongoDB server to connect to. Default: `localhost` 
- `DbPort` Port of the MongoDB server to connect to. Default: `27017` 
- `DbUser` Username if the MongoDB server requires authentication. 
- `DbPassword` Password if the MongoDB server requires authentication. 
- `DbConnectionString` alternative to the above options to provide the connection string manually e.g. `mongodb+srv://user:password@my.cluster.mongodb.net/?retryWrites=true&w=majority`
- `BindHost` Ip/hostname to bind the proxy to. Default: `*` 
- `BindPort` Port the proxy will listen on. Default: `8008` 
- `IPWhitelist` Restrict the IPs that are allowed to call the proxy as a `,` seperated list. Default: `No restrictions`
