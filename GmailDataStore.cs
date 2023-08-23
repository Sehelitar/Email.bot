using Google.Apis.Json;
using Google.Apis.Util.Store;
using Streamer.bot.Plugin.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmailBot
{
    internal class GmailDataStore : IDataStore
    {
        private readonly IInlineInvokeProxy BotProxy;
        private readonly Dictionary<string, string> DataStore;
        private readonly DateTime Expiration;

        public GmailDataStore(IInlineInvokeProxy botProxy)
        {
            BotProxy = botProxy;
            DataStore = new Dictionary<string, string>();
            Expiration = DateTime.Now.AddDays(7);

            try
            {
                Expiration = BotProxy.GetGlobalVar<DateTime>("GmailTokenExpiration");
                if (Expiration != null && Expiration.Subtract(DateTime.Now).TotalDays >= 1)
                {
                    BotProxy.LogVerbose("[Email.bot] Cache is valid. Loading DataStore...");
                    string json = BotProxy.GetGlobalVar<string>("GmailToken", true);
                    if (json != null)
                    {
                        DataStore = json.ToUnprotectedObject<Dictionary<string, string>>();
                    }
                }
                else
                {
                    BotProxy.LogVerbose("[Email.bot] Cache is either invalid or expired. Authorization will be renewed.");
                    BotProxy.UnsetGlobalVar("GmailToken");
                    BotProxy.UnsetGlobalVar("GmailTokenExpiration");
                    Expiration = DateTime.Now.AddDays(7);
                }
            }
            catch (Exception e)
            {
                BotProxy.LogDebug("[Email.bot] An error occured during DataStore initialization : " + e);
            }
        }

        private void Persist()
        {
            BotProxy.LogVerbose("[Email.bot] Saving cache data...");
            BotProxy.SetGlobalVar("GmailToken", DataStore.ToProtectedData());
            BotProxy.SetGlobalVar("GmailTokenExpiration", Expiration);
        }

        public Task ClearAsync()
        {
            DataStore.Clear();
            Persist();
            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            DataStore.Remove(key);
            Persist();
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            if (DataStore.TryGetValue(key, out var value))
            {
                return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(value));
            }
            else
            {
                return Task.FromResult((T)default);
            }
        }

        public Task StoreAsync<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Key MUST have a value");
            }

            DataStore.Remove(key);
            DataStore.Add(key, NewtonsoftJsonSerializer.Instance.Serialize(value));
            Persist();
            return Task.CompletedTask;
        }
    }
}
