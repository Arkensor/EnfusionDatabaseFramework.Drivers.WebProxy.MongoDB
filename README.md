## EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB
MongoDB web proxy implementation for the [EnfusionDatabaseFramework](https://github.com/Arkensor/EnfusionDatabaseFramework).

### Options
- `DbHost` Hostname of the MongoDB server to connect to. Default: `localhost` 
- `DbPort` Port of the MongoDB server to connect to. Default: `27017` 
- `DbUser` Username if the MongoDB server requires authentication. 
- `DbPassword` Password if the MongoDB server requires authentication. 
- `DbConnectionString` alternative to the above options to provide the connection string manually e.g. `mongodb+srv://user:password@my.cluster.mongodb.net/?retryWrites=true&w=majority`
- `BindHost` Ip/hostname to bind the proxy to. Default: `*` 
- `BindPort` Port the proxy will listen on. Default: `8008` 
- `IPWhitelist` Restrict the IPs that are allowed to call the proxy as a `,` seperated list. Default: `No restrictions`
