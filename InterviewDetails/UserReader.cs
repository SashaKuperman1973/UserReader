using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Timers;

namespace InterviewDetails
{
    public class FuncObject<T>
    {
        protected readonly Func<object> ValueFunc;

        public FuncObject(Func<object> valueFunc)
        {
            this.ValueFunc = valueFunc;
        }

        public virtual T Value => (T) this.ValueFunc();
    }

    public class CachedObject<T> : FuncObject<T>
    {
        private readonly ICache cache;
        private readonly object key;

        public CachedObject(ICache cache, object key, Func<object> valueFunc) : base(valueFunc)
        {
            this.cache = cache;
            this.key = key;
        }

        public override T Value => (T)this.cache.GetOrAdd(this.key, this.ValueFunc);
    }

    public class User
    {
        [CustomPersistenceType(RecordKey = "Email")]
        public FuncObject<string> UserName { get; set; }

        [CustomPersistenceType(RecordKey = "Id")]
        public FuncObject<Guid> Key { get; set; }

        [SomeBusinessUsePersistenceType(RecordKey = "InterestingBusinessField")]
        public FuncObject<string> UserBusinessField { get; set; }

        public override string ToString()
        {
            return
                "UserName: " + this.UserName.Value +
                "\nID: " + this.Key.Value +
                "\nUserBusinessField: " + this.UserBusinessField.Value;
        }
    }

    public interface IDiContaner
    {
        T Resolve<T>() where T : class;
    }

    public class DiContainer : IDiContaner
    {
        private static readonly ContactCollectionReader ContactCollectionReader = new ContactCollectionReader();
        private static readonly SomeBusinessUseCollectionReader SomeBusinessUseCollectionReader = new SomeBusinessUseCollectionReader();

        public T Resolve<T>() where T : class
        {
            if (typeof(T) == typeof(ContactCollectionReader))
            {
                return DiContainer.ContactCollectionReader as T;
            }

            if (typeof(T) == typeof(SomeBusinessUseCollectionReader))
            {
                return DiContainer.SomeBusinessUseCollectionReader as T;
            }

            throw new ArgumentException("Cannot find dependency: " + typeof(T));
        }
    }

    public class ExpirableValue
    {
        public DateTime ExpiryTime { get; set; }
        public object Value { get; set; }

        public ExpirableValue(DateTime expiryTime, object value)
        {
            this.ExpiryTime = expiryTime;
            this.Value = value;
        }
    }

    public interface ICache
    {
        object GetOrAdd(object key, Func<object> valueFunc);
    }

    public class Cache : ICache
    {
        private readonly Timer timer = new Timer();

        private readonly ConcurrentDictionary<object, ExpirableValue> backing = new ConcurrentDictionary<object,ExpirableValue>();

        public Cache()
        {
            this.timer.AutoReset = true;
            this.timer.Interval = 1000;
            this.timer.Elapsed += this.CleanCache;
            this.timer.Start();
        }

        private void CleanCache(object sender, ElapsedEventArgs args)
        {
            List<object> keyList = (from entry in this.backing
                where entry.Value.ExpiryTime <= DateTime.Now
                select entry.Key).ToList();

            foreach (object key in keyList)
            {
                ExpirableValue removed;
                this.backing.TryRemove(key, out removed);
                Console.WriteLine("Removed: " + removed.Value);
            }
        }

        ~Cache()
        {
            this.timer.Stop();
            this.timer.Elapsed -= this.CleanCache;
            this.timer.Dispose();
        }

        public object GetOrAdd(object key, Func<object> valueFunc)
        {
            var cacheHit = true;
            object result = this.backing.GetOrAdd(key, keyParam =>
            {
                cacheHit = false;
                return new ExpirableValue(DateTime.Now.AddSeconds(2), valueFunc());
            }).Value;

            Console.WriteLine("Cache Hit: " + cacheHit);

            return result;
        }
    }

    public interface ICollectionReader
    {
        IDictionary<string, object> GetCollection();
    }

    public class ContactCollectionReader : ICollectionReader
    {
        private static readonly Guid Id = Guid.NewGuid();

        public IDictionary<string, object> GetCollection()
        {
            var result = new Dictionary<string, object>
            {
                { "Email", "myname@domain.com" },
                { "Id", ContactCollectionReader.Id },
            };

            Console.WriteLine("Contact collection read");

            return result;
        }
    }

    public class SomeBusinessUseCollectionReader : ICollectionReader
    {
        public IDictionary<string, object> GetCollection()
        {
            var result = new Dictionary<string, object>
            {
                {"InterestingBusinessField", "Interesting Business Fact"}
            };

            Console.WriteLine("Business collection read");

            return result;
        }
    }

    public interface IPersistenceTypeAttribute
    {
        string RecordKey { get; set; }

        ICollectionReader GetCollectionReader(IDiContaner collectionReaderFactory);
    }

    public class CustomPersistenceTypeAttribute : Attribute, IPersistenceTypeAttribute
    {
        public string RecordKey { get; set; }

        public ICollectionReader GetCollectionReader(IDiContaner collectionReaderFactory)
        {
            return collectionReaderFactory.Resolve<ContactCollectionReader>();
        }
    }

    public class SomeBusinessUsePersistenceTypeAttribute : Attribute, IPersistenceTypeAttribute
    {
        public string RecordKey { get; set; }

        public ICollectionReader GetCollectionReader(IDiContaner collectionReaderFactory)
        {
            return collectionReaderFactory.Resolve<SomeBusinessUseCollectionReader>();
        }
    }

    public class UserReader<T> where T : new()
    {
        private readonly IDiContaner collectionReaderFactory;
        private readonly ICache cache;

        public UserReader(IDiContaner collectionReaderFactory, ICache cache)
        {
            this.collectionReaderFactory = collectionReaderFactory;
            this.cache = cache;
        }

        public T ReadFromPersistence()
        {
            var result = new T();

            List<PropertyInfo> userPropertyInfos = typeof(T).GetProperties().ToList();

            userPropertyInfos.ForEach(userPropertyInfo =>
            {
                var persistenceTypeAttribute = (IPersistenceTypeAttribute) userPropertyInfo.GetCustomAttributes()
                    .FirstOrDefault(attribute => attribute is IPersistenceTypeAttribute);

                if (persistenceTypeAttribute == null)
                {
                    return;
                }

                ICollectionReader collectionReader = persistenceTypeAttribute.GetCollectionReader(this.collectionReaderFactory);

                IDictionary<string, object> CollectionFunc() => collectionReader.GetCollection();

                var cachedCollection = new CachedObject<IDictionary<string, object>>(this.cache, collectionReader, CollectionFunc);

                Type funcObjectGenericType = userPropertyInfo.PropertyType.GetGenericArguments()[0];

                Type funcObjectPropertyType = typeof(FuncObject<>).MakeGenericType(new[] { funcObjectGenericType });

                object funcProperty = Activator.CreateInstance(funcObjectPropertyType,
                    (Func<object>) (() =>
                        {
                            return cachedCollection.Value[persistenceTypeAttribute.RecordKey ?? userPropertyInfo.Name];
                        }
                    ));

                userPropertyInfo.SetValue(result, funcProperty);
            });

            return result;
        }
    }
}
