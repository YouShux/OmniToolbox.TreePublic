using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Plugin;
using OmenTools.OmenService;
using OmniToolbox.Host;

namespace OmniToolbox.TreePublic;

internal sealed class MultiToolbarPluginBridge
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private object? pluginManagerInstance;
    private PropertyInfo? installedPluginsProperty;
    private object? profileManagerInstance;
    private PropertyInfo? defaultProfileProperty;
    private PropertyInfo? profilesProperty;
    private MethodInfo? addNewProfileMethod;
    private MethodInfo? deleteProfileAsyncMethod;
    private PropertyInfo? profileGUIDProperty;
    private PropertyInfo? profileNameProperty;
    private PropertyInfo? profileIsDefaultProperty;
    private PropertyInfo? profileIsEnabledProperty;
    private PropertyInfo? profilePluginsProperty;
    private PropertyInfo? profilePluginInternalNameProperty;
    private MethodInfo? profileAddOrUpdateAsyncMethod;
    private MethodInfo? profileRemoveAsyncMethod;
    private MethodInfo? profileSetStateAsyncMethod;
    private FieldInfo? profileManagerConfigField;
    private MethodInfo? queueSaveMethod;

    public MultiToolbarPluginBridge()
    {
        InitializeServices();
    }

    public async Task<bool> TogglePluginAsync(IExposedPlugin plugin, bool targetState)
    {
        if (!IsInitialized)
        {
            InitializeServices();
        }

        try
        {
            if (!IsInitialized)
            {
                return false;
            }

            var workingPluginID = FindWorkingPluginID(plugin.InternalName);
            if (workingPluginID == Guid.Empty)
            {
                DalamudServices.PluginLog.Warning(
                    "MultiToolbar could not find the working plugin ID for {PluginName}.", plugin.InternalName);
                return false;
            }

            var profile = defaultProfileProperty!.GetValue(profileManagerInstance);
            if (profile is null)
            {
                return false;
            }

            return await AwaitTask(profileAddOrUpdateAsyncMethod!.Invoke(profile,
                [workingPluginID, plugin.InternalName, targetState, true])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to toggle plugin {PluginName}.", plugin.InternalName);
            return false;
        }
    }

    public List<MultiToolbarCollectionConfig> ReadCollections()
    {
        if (!TryEnsureInitialized() || profilesProperty?.GetValue(profileManagerInstance) is not IEnumerable profiles)
        {
            return [];
        }

        var result = new List<MultiToolbarCollectionConfig>();
        foreach (var profile in profiles)
        {
            if (profile is not null && ReadCollection(profile) is { } collection)
            {
                result.Add(collection);
            }
        }

        return result;
    }

    public bool ReorderCollections(IReadOnlyList<Guid> orderedProfileIDs)
    {
        if (!TryEnsureInitialized() || profilesProperty?.GetValue(profileManagerInstance) is not IList profiles ||
            profileGUIDProperty is null)
        {
            return false;
        }

        var ranks = orderedProfileIDs
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index);
        var reordered = profiles.Cast<object>()
            .OrderBy(profile => ranks.GetValueOrDefault(
                ReadGUID(profileGUIDProperty.GetValue(profile)), int.MaxValue))
            .ToArray();
        for (var index = 0; index < reordered.Length; index++)
        {
            profiles[index] = reordered[index];
        }

        QueueProfileSave();
        return true;
    }

    public bool TryCreateCollection(string name, out MultiToolbarCollectionConfig? collection)
    {
        collection = null;
        if (!TryEnsureInitialized() || addNewProfileMethod is null || profileNameProperty is null)
        {
            return false;
        }

        try
        {
            var profile = addNewProfileMethod.Invoke(profileManagerInstance, null);
            if (profile is null)
            {
                return false;
            }

            profileNameProperty.SetValue(profile, name);
            QueueProfileSave();
            collection = ReadCollection(profile);
            return collection is not null;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to create a Dalamud profile.");
            return false;
        }
    }

    public async Task<bool> CopyCollectionAsync(IReadOnlyList<string> pluginNames, string name)
    {
        if (!TryEnsureInitialized())
        {
            return false;
        }

        Guid? createdProfileID = null;
        try
        {
            var members = new List<(Guid ID, string Name)>();
            foreach (var pluginName in pluginNames)
            {
                var pluginID = FindWorkingPluginID(pluginName);
                if (pluginID == Guid.Empty)
                {
                    continue;
                }
                members.Add((pluginID, pluginName));
            }

            if (!TryCreateCollection(name, out var copy) || copy?.DalamudProfileID is not { } profileID)
            {
                return false;
            }
            createdProfileID = profileID;
            if (!await SetCollectionEnabledAsync(profileID, false).ConfigureAwait(false))
            {
                return false;
            }
            var profile = FindProfile(profileID);
            if (profile is null)
            {
                return false;
            }
            foreach (var member in members)
            {
                if (!await AwaitTask(profileAddOrUpdateAsyncMethod!.Invoke(profile,
                        [member.ID, member.Name, true, true])).ConfigureAwait(false))
                {
                    return false;
                }
            }
            QueueProfileSave();
            createdProfileID = null;
            return true;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to copy collection {Collection}.", name);
            return false;
        }
        finally
        {
            if (createdProfileID is { } profileID)
            {
                await DeleteCollectionAsync(profileID).ConfigureAwait(false);
            }
        }
    }

    public bool TryRenameCollection(Guid profileID, string name)
    {
        if (!TryEnsureInitialized() || profileNameProperty is null || FindProfile(profileID) is not { } profile)
        {
            return false;
        }

        try
        {
            profileNameProperty.SetValue(profile, name);
            QueueProfileSave();
            return true;
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to rename a Dalamud profile.");
            return false;
        }
    }

    public async Task<bool> DeleteCollectionAsync(Guid profileID)
    {
        if (!TryEnsureInitialized() || deleteProfileAsyncMethod is null || FindProfile(profileID) is not { } profile)
        {
            return false;
        }

        try
        {
            return await AwaitTask(deleteProfileAsyncMethod.Invoke(profileManagerInstance, [profile]))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to delete a Dalamud profile.");
            return false;
        }
    }

    public async Task<bool> SetCollectionEnabledAsync(Guid profileID, bool enabled)
    {
        if (!TryEnsureInitialized() || profileSetStateAsyncMethod is null || FindProfile(profileID) is not { } profile)
        {
            return false;
        }

        try
        {
            return await AwaitTask(profileSetStateAsyncMethod.Invoke(profile, [enabled, true]))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to change a Dalamud profile state.");
            return false;
        }
    }

    public async Task<bool> ApplyCollectionAsync(Guid profileID)
    {
        return await SetCollectionEnabledAsync(profileID, true).ConfigureAwait(false);
    }

    public async Task<bool> UpdateCollectionPluginAsync(Guid profileID, IExposedPlugin plugin, bool selected)
    {
        if (!TryEnsureInitialized() || FindProfile(profileID) is not { } profile)
        {
            return false;
        }

        try
        {
            var workingPluginID = FindWorkingPluginID(plugin.InternalName);
            if (workingPluginID == Guid.Empty)
            {
                return false;
            }

            var task = selected
                ? profileAddOrUpdateAsyncMethod?.Invoke(profile, [workingPluginID, plugin.InternalName, true, true])
                : profileRemoveAsyncMethod?.Invoke(profile, [workingPluginID, true, true]);
            return await AwaitTask(task).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to update a Dalamud profile plugin.");
            return false;
        }
    }

    private bool IsInitialized => pluginManagerInstance is not null && installedPluginsProperty is not null &&
                                  profileManagerInstance is not null && defaultProfileProperty is not null &&
                                  profileAddOrUpdateAsyncMethod is not null;

    private bool TryEnsureInitialized()
    {
        if (!IsInitialized)
        {
            InitializeServices();
        }

        return IsInitialized;
    }

    private void InitializeServices()
    {
        try
        {
            var assembly = typeof(IDalamudPlugin).Assembly;
            var serviceType = assembly.GetType("Dalamud.Service`1");
            var pluginManagerType = assembly.GetType("Dalamud.Plugin.Internal.PluginManager");
            if (serviceType is not null && pluginManagerType is not null)
            {
                pluginManagerInstance = serviceType.MakeGenericType(pluginManagerType)
                    .GetMethod("Get", StaticFlags)?.Invoke(null, null);
                installedPluginsProperty = pluginManagerType.GetProperty("InstalledPlugins", InstanceFlags);
            }

            var profileManagerType = assembly.GetType("Dalamud.Plugin.Internal.Profiles.ProfileManager");
            if (serviceType is not null && profileManagerType is not null)
            {
                profileManagerInstance = serviceType.MakeGenericType(profileManagerType)
                    .GetMethod("Get", StaticFlags)?.Invoke(null, null);
                defaultProfileProperty = profileManagerType.GetProperty("DefaultProfile", InstanceFlags);
                profilesProperty = profileManagerType.GetProperty("Profiles", InstanceFlags);
                addNewProfileMethod = profileManagerType.GetMethod("AddNewProfile", InstanceFlags);
                deleteProfileAsyncMethod = profileManagerType.GetMethod("DeleteProfileAsync", InstanceFlags);
                profileManagerConfigField = profileManagerType.GetField("config", InstanceFlags);
                queueSaveMethod = profileManagerConfigField?.FieldType.GetMethod("QueueSave", InstanceFlags);
            }

            var profileType = assembly.GetType("Dalamud.Plugin.Internal.Profiles.Profile");
            if (profileType is not null)
            {
                profileGUIDProperty = profileType.GetProperty("Guid", InstanceFlags);
                profileNameProperty = profileType.GetProperty("Name", InstanceFlags);
                profileIsDefaultProperty = profileType.GetProperty("IsDefaultProfile", InstanceFlags);
                profileIsEnabledProperty = profileType.GetProperty("IsEnabled", InstanceFlags);
                profilePluginsProperty = profileType.GetProperty("Plugins", InstanceFlags);
                profileAddOrUpdateAsyncMethod = profileType.GetMethod(
                    "AddOrUpdateAsync",
                    InstanceFlags,
                    binder: null,
                    types: [typeof(Guid), typeof(string), typeof(bool), typeof(bool)],
                    modifiers: null);
                profileRemoveAsyncMethod = profileType.GetMethod(
                    "RemoveAsync",
                    InstanceFlags,
                    binder: null,
                    types: [typeof(Guid), typeof(bool), typeof(bool)],
                    modifiers: null);
                profileSetStateAsyncMethod = profileType.GetMethod(
                    "SetStateAsync",
                    InstanceFlags,
                    binder: null,
                    types: [typeof(bool), typeof(bool)],
                    modifiers: null);

                var pluginEntryType = profilePluginsProperty?.PropertyType.GetGenericArguments() is { Length: > 0 } arguments
                    ? arguments[0]
                    : null;
                profilePluginInternalNameProperty = pluginEntryType?.GetProperty("InternalName", InstanceFlags);
            }
        }
        catch (Exception ex)
        {
            pluginManagerInstance = null;
            installedPluginsProperty = null;
            profileManagerInstance = null;
            defaultProfileProperty = null;
            profilesProperty = null;
            addNewProfileMethod = null;
            deleteProfileAsyncMethod = null;
            profileGUIDProperty = null;
            profileNameProperty = null;
            profileIsDefaultProperty = null;
            profileIsEnabledProperty = null;
            profilePluginsProperty = null;
            profilePluginInternalNameProperty = null;
            profileAddOrUpdateAsyncMethod = null;
            profileRemoveAsyncMethod = null;
            profileSetStateAsyncMethod = null;
            profileManagerConfigField = null;
            queueSaveMethod = null;
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to initialize the plugin manager bridge.");
        }
    }

    private MultiToolbarCollectionConfig? ReadCollection(object profile)
    {
        if (profileGUIDProperty is null || profileNameProperty is null || profileIsDefaultProperty is null)
        {
            return null;
        }

        if (profileIsDefaultProperty.GetValue(profile) is true ||
            profileNameProperty.GetValue(profile) is not string name || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var profileID = ReadGUID(profileGUIDProperty.GetValue(profile));
        if (profileID == Guid.Empty)
        {
            return null;
        }

        var pluginNames = new List<string>();
        if (profilePluginsProperty?.GetValue(profile) is IEnumerable plugins && profilePluginInternalNameProperty is not null)
        {
            foreach (var entry in plugins)
            {
                if (entry is not null && profilePluginInternalNameProperty.GetValue(entry) is string internalName &&
                    !string.IsNullOrWhiteSpace(internalName))
                {
                    pluginNames.Add(internalName);
                }
            }
        }

        return new MultiToolbarCollectionConfig
        {
            Name = name,
            Enabled = profileIsEnabledProperty?.GetValue(profile) is not false,
            PluginInternalNames = pluginNames,
            DalamudProfileID = profileID
        };
    }

    private object? FindProfile(Guid profileID)
    {
        if (profilesProperty?.GetValue(profileManagerInstance) is not IEnumerable profiles || profileGUIDProperty is null)
        {
            return null;
        }

        foreach (var profile in profiles)
        {
            if (profile is not null && ReadGUID(profileGUIDProperty.GetValue(profile)) == profileID)
            {
                return profile;
            }
        }

        return null;
    }

    private void QueueProfileSave()
    {
        try
        {
            var config = profileManagerConfigField?.GetValue(profileManagerInstance);
            queueSaveMethod?.Invoke(config, null);
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Error(ex, "MultiToolbar failed to save Dalamud profiles.");
        }
    }

    private Guid FindWorkingPluginID(string internalName)
    {
        if (installedPluginsProperty?.GetValue(pluginManagerInstance) is not IEnumerable plugins)
        {
            return Guid.Empty;
        }

        foreach (var rawPlugin in plugins)
        {
            if (rawPlugin is null)
            {
                continue;
            }

            var type = rawPlugin.GetType();
            var name = type.GetProperty("InternalName", InstanceFlags)?.GetValue(rawPlugin) as string;
            if (!string.Equals(name, internalName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pluginID = ReadGUID(type.GetProperty("EffectiveWorkingPluginId", InstanceFlags)?.GetValue(rawPlugin));
            if (pluginID != Guid.Empty)
            {
                return pluginID;
            }

            var manifest = type.GetProperty("Manifest", InstanceFlags)?.GetValue(rawPlugin);
            return ReadGUID(manifest?.GetType().GetProperty("WorkingPluginId", InstanceFlags)?.GetValue(manifest));
        }

        return Guid.Empty;
    }

    private static async Task<bool> AwaitTask(object? value)
    {
        if (value is not Task task)
        {
            return false;
        }

        await task.ConfigureAwait(false);
        return true;
    }

    private static Guid ReadGUID(object? value) => value is Guid guid ? guid : Guid.Empty;
}
