# ConnectorSageBitrix Installation and Setup Guide

This guide provides step-by-step instructions for installing, configuring, and managing the ConnectorSageBitrix Windows Service.

## Prerequisites

- Windows 8/10/11 or Windows Server 2016/2019/2022
- .NET Framework 4.7.2 or higher
- SQL Server with Sage 200c database
- Bitrix24 account with API access
- Appropriate licenses for both systems
- Administrator access to the server

## Build Instructions

1. Open the solution in Visual Studio
2. Restore NuGet packages:
   ```
   nuget restore ConnectorSageBitrix.sln
   ```
3. Build the solution:
   ```
   msbuild ConnectorSageBitrix.sln /p:Configuration=Release
   ```
4. The executable and related files will be in the `bin\Release` folder

## Configuration

### Using App.config

Edit the `App.config` file to customize your connection settings:

```xml
<appSettings>
  <!-- Database configuration -->
  <add key="DB_HOST" value="YourSQLServer\Instance" />
  <add key="DB_PORT" value="1433" />
  <add key="DB_DATABASE" value="YourDatabase" />
  <add key="DB_USERNAME" value="YourUsername" />
  <add key="DB_PASSWD" value="YourPassword" />
  
  <!-- License and client information -->
  <add key="LICENSE_ID" value="your-license-id" />
  <add key="CLIENT_CODE" value="your-client-code" />
  <add key="BITRIX_CLIENT_CODE" value="your-bitrix-client-code" />
  
  <!-- Bitrix24 configuration -->
  <add key="BITRIX_URL" value="your-bitrix24-api-url" />
  
  <!-- Synchronization settings -->
  <add key="PACK_EMPRESA" value="true" />
  
  <!-- Sync interval in minutes (optional, default is 5) -->
  <add key="SYNC_INTERVAL_MINUTES" value="5" />
</appSettings>
```

### Using Encrypted Configuration

For enhanced security, you can use the encrypted configuration file:

1. Create the configuration in JSON format
2. Encrypt it using the BTic encryption tool
3. Place the encrypted file at:
   ```
   C:\ProgramData\Btic\ConfigConnectorBitrix\config
   ```

## Service Installation

### Using InstallUtil

1. Open a command prompt with administrator privileges
2. Navigate to the .NET Framework directory:
   ```
   cd %windir%\Microsoft.NET\Framework64\v4.0.30319
   ```
3. Run InstallUtil:
   ```
   InstallUtil.exe "C:\path\to\ConnectorSageBitrix.exe"
   ```
4. Enter credentials when prompted (for LocalSystem account, just press Enter)

### Uninstalling the Service

```
%windir%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe /u "C:\path\to\ConnectorSageBitrix.exe"
```

## Service Management

### Using Services Console

1. Open Services console (services.msc)
2. Find "ConnectorSageBitrix" in the list
3. Right-click and select Start, Stop, or Restart as needed
4. To change startup type, right-click, select Properties, and modify the "Startup type" setting

### Using Command Line

```
net start ConnectorSageBitrix
net stop ConnectorSageBitrix
```

## Testing the Service

You can run the service in console mode for testing:

```
ConnectorSageBitrix.exe -test
```

This runs the service in the foreground with console output, allowing you to see logs and debug issues.

## Licensing

Ensure your license file is in the correct location:

```
C:\ProgramData\Btic\licenses\[LICENSE_ID].txt
```

The license is checked at startup and periodically (every 24 hours) to ensure it remains valid.

## Logging

Logs are stored in:

```
C:\ProgramData\ConnectorSageBitrix\connector.log
```

Check this file for detailed operation information and troubleshooting.

## Troubleshooting

### Service Won't Start

1. Check Windows Event Viewer (eventvwr.msc) for error details
2. Verify database connection settings in App.config
3. Ensure license file exists and is valid
4. Check permissions on log directory

### Synchronization Issues

1. Verify the PACK_EMPRESA setting is set to "true"
2. Check Bitrix24 API URL and ensure it's accessible
3. Verify SQL Server connection and permissions
4. Check the log file for specific synchronization errors

### License Validation Failures

1. Confirm the license file exists at the specified path
2. Verify the CLIENT_CODE matches the license
3. Ensure the service has internet access to validate the license

## Support

For additional assistance, contact BTic-Consultoria support.
