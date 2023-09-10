using System;
using System.Threading.Tasks;
using Tmds.DBus;

namespace systemd1.DBus;

[DBusInterface("org.freedesktop.systemd1.Scope")]
interface IJob : IDBusObject
{
    Task<T> GetAsync<T>(string prop);
    Task SetAsync(string prop, object val);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}
