# Playtime-Plugin
Simple plugin for scp:sl for Exiled. It saves a playtime of every player on server to database. 

## Configuration
```cs
public bool IsEnabled { get; set; } = true;
public bool Debug { get; set; } = false;
public string ConnectionString { get; set; } = "Server=IP_OF_YOUR_DATABASE;Port=YOUR_DATABASE_PORT;Database=DATABASE_NAME;Uid=YOUR_DATABASE_USER;Pwd=NAME_OF_YOUR_DATABASE_PASSWORD;";
```
