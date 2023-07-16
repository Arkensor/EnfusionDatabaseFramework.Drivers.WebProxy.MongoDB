## EnfusionDatabaseFramework.Drivers.WebProxy.MongoDB
MongoDB web proxy implementation for the [EnfusionDatabaseFramework](https://github.com/Arkensor/EnfusionDatabaseFramework).

### Options
- `DbHost` Hostname of the MongoDB server to connect to. Default: `localhost`
- `DbPort` Port of the MongoDB server to connect to. Default: `27017`
- `BindHost` Ip/hostname to bind to. Default: `*`
- `BindPort` Port to listen on. Default: `8008`
- `AllowedHosts` Allowed hosts is a semicolon-delimited list of host names without port numbers. Requests without a matching host name will be refused. Host names may be prefixed with a '*: wildcard, or use '*' to allow all hosts. Default: `*`
