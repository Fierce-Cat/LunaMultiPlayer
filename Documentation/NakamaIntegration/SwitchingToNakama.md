# Switching to Nakama Backend

This guide explains how to switch the LunaMultiPlayer client from the legacy Lidgren UDP backend to the new Nakama WebSocket backend.

## Prerequisites

Before switching the client, ensure you have a local Nakama server running.

1.  Navigate to the `nakama/` directory in the repository.
2.  Follow the instructions in `nakama/README.md` to start the server using Docker:
    ```bash
    cd nakama
    docker-compose up -d
    ```
3.  Verify the server is running by accessing the admin console at `http://localhost:7351`.

## 1. Current Method (Code Change)

Currently, the network backend is determined by a hardcoded value in the `NetworkMain` class. To switch to Nakama, you must modify the source code and recompile the client.

**File:** `LmpClient/Network/NetworkMain.cs`

Locate the `AwakeNetworkSystem` method (around line 103) and modify the `NetworkConnectionFactory.Create` call:

```csharp
// LmpClient/Network/NetworkMain.cs

public static void AwakeNetworkSystem()
{
    // ... existing code ...

    // Use the factory to create the connection
    
    // COMMENT OUT LIDGREN:
    // ClientConnection = NetworkConnectionFactory.Create(NetworkConnectionFactory.NetworkBackend.Lidgren);
    
    // UNCOMMENT NAKAMA:
    ClientConnection = NetworkConnectionFactory.Create(NetworkConnectionFactory.NetworkBackend.Nakama);
}
```

Rebuild the `LmpClient` project for the changes to take effect.

## 2. Future Method (Settings Proposal)

To allow users to switch backends without recompiling, we propose adding a `NetworkBackend` setting to the XML configuration.

### Proposed Changes

**1. Update `SettingsStructures.cs`**

Add a `NetworkBackend` property to the `SettingStructure` class in `LmpClient/Systems/SettingsSys/SettingsStructures.cs`:

```csharp
public class SettingStructure
{
    // ... existing settings ...
    
    // New Setting
    public int NetworkBackend { get; set; } = 0; // 0 = Lidgren, 1 = Nakama
}
```

**2. Update `NetworkMain.cs`**

Modify `NetworkMain.cs` to read from the settings system instead of using a hardcoded value:

```csharp
public static void AwakeNetworkSystem()
{
    // ... 
    
    var backend = (NetworkConnectionFactory.NetworkBackend)SettingsSystem.CurrentSettings.NetworkBackend;
    ClientConnection = NetworkConnectionFactory.Create(backend);
}
```

**3. XML Configuration**

Users will then be able to switch backends by editing `LmpClient.xml` (generated in the KSP GameData folder):

```xml
<SettingStructure>
    ...
    <NetworkBackend>1</NetworkBackend> <!-- 0 for Lidgren, 1 for Nakama -->
    ...
</SettingStructure>