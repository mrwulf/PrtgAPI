﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using PrtgAPI.Attributes;
using PrtgAPI.Helpers;
using PrtgAPI.Objects.Deserialization;
using PrtgAPI.Objects.Shared;
using PrtgAPI.Objects.Undocumented;
using PrtgAPI.Parameters;
using PrtgAPI.Request;

namespace PrtgAPI
{
    /// <summary>
    /// <para type="description">Provides methods for generating API requests against a PRTG Network Monitor server.</para>
    /// </summary>
    public partial class PrtgClient
    {
        /// <summary>
        /// Stores server and authentication details required to connect to a PRTG Server.
        /// </summary>
        internal readonly ConnectionDetails connectionDetails;

        /// <summary>
        /// Provides methods for retrieving dynamic sensor targets used for creating and modifying sensors.
        /// </summary>
        public PrtgTargetHelper Targets { get; private set; }

        /// <summary>
        /// The PRTG server API requests will be made against.
        /// </summary>
        public string Server => connectionDetails.Server;

        /// <summary>
        /// The Username that will be used to authenticate against PRTG.
        /// </summary>
        public string UserName => connectionDetails.UserName;

        /// <summary>
        /// The PassHash that will be used to authenticate with, in place of a password.
        /// </summary>
        public string PassHash => connectionDetails.PassHash;

        /// <summary>
        /// The number of times to retry a request that times out while communicating with PRTG.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// The base delay (in seconds) between retrying a timed out request. Each successive failure of a given request will wait an additional multiple of this value.
        /// </summary>
        public int RetryDelay { get; set; }

        internal EventHandler<RetryRequestEventArgs> retryRequest;

        /// <summary>
        /// Occurs when a request times out while communicating with PRTG.
        /// </summary>
        public event EventHandler<RetryRequestEventArgs> RetryRequest
        {
            add { retryRequest += value; }
            remove { retryRequest -= value; }
        }

        internal EventHandler<LogVerboseEventArgs> logVerbose;

        /// <summary>
        /// Occurs when a PrtgAPI logs verbose processing information.
        /// </summary>
        public event EventHandler<LogVerboseEventArgs> LogVerbose
        {
            add { logVerbose += value; }
            remove { logVerbose -= value; }
        }

        /// <summary>
        /// Specifies the version of the PRTG Server this client is connected to.
        /// </summary>
        public Version Version => version ?? (version = GetStatus().Version);

        private Version version;

        internal void Log(string message)
        {
            HandleEvent(logVerbose, new LogVerboseEventArgs(message));
        }

        internal void HandleEvent<T>(EventHandler<T> handler, T args)
        {
            handler?.Invoke(this, args);
        }

        private RequestEngine requestEngine;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrtgClient"/> class.
        /// </summary>
        /// <param name="server">The server to connect to. If a protocol is not specified, HTTPS will be used.</param>
        /// <param name="username">The username to authenticate with.</param>
        /// <param name="password">The password or passhash to authenticate with.</param>
        /// <param name="authMode">Whether the <paramref name="password"/> refers to a password or passhash. If a password is specified,
        /// this will automatically be resolved to a passhash.</param>
        public PrtgClient(string server, string username, string password, AuthMode authMode = AuthMode.Password)
            : this(server, username, password, authMode, new PrtgWebClient())
        {
        }

        internal PrtgClient(string server, string username, string password, AuthMode authMode, IWebClient client)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));

            if (username == null)
                throw new ArgumentNullException(nameof(username));

            if (password == null)
                throw new ArgumentNullException(nameof(password));

            requestEngine = new RequestEngine(this, client);

            connectionDetails = new ConnectionDetails(server, username, password);
            Targets = new PrtgTargetHelper(this);

            if (authMode == AuthMode.Password)
                connectionDetails.PassHash = GetPassHash(password);
        }

#region Requests

        internal VersionClient GetVersionClient(object[] obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var enums = obj.Where(o => o is Enum).ToList();

            if (enums.Count == 0)
                throw new NotImplementedException($"Don't know how to get {nameof(VersionAttribute)} for '{string.Join(",", obj)}'");

            var result = obj.OfType<Enum>().Select(o => o.GetEnumAttribute<VersionAttribute>()).Where(a => a != null).OrderBy(a => a.Version).ToList();

            var attr = result.FirstOrDefault();
            var ver = attr?.Version ?? RequestVersion.v14_4;

            if (attr != null && attr.IsActive(Version))
            {
                switch (ver)
                {
                    case RequestVersion.v18_1:
                        return new VersionClient18_1(this);

                    default:
                        return new VersionClient(ver, this);
                }
            }
            else
                return new VersionClient(ver, this);
        }

        [ExcludeFromCodeCoverage]
        internal VersionClient GetVersionClient<T1, T2>(List<T1> parameters) where T1 : PropertyParameter<T2>
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            return GetVersionClient(parameters.Select(p => p.Property).Cast<object>().ToArray());
        }

    #region Object Data

        private string GetPassHash(string password)
        {
            var parameters = new Parameters.Parameters()
            {
                [Parameter.Password] = password
            };

            var response = requestEngine.ExecuteRequest(JsonFunction.GetPassHash, parameters);

            if(!Regex.Match(response, "^[0-9]+$").Success)
                throw new PrtgRequestException($"Could not retrieve PassHash from PRTG Server. PRTG responded '{response}'");

            return response;
        }

        #region Get Objects

        internal List<T> GetObjects<T>(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null) =>
            GetObjectsRaw<T>(parameters, function, responseValidator).Items;

        private XmlDeserializer<T> GetObjectsRaw<T>(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null)
        {
            var response = requestEngine.ExecuteRequest(function, parameters, responseValidator);

            return SetVersion(XmlDeserializer<T>.DeserializeList(response));
        }

        internal async Task<List<T>> GetObjectsAsync<T>(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null) =>
            (await GetObjectsRawAsync<T>(parameters, function, responseValidator).ConfigureAwait(false)).Items;

        private async Task<XmlDeserializer<T>> GetObjectsRawAsync<T>(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null)
        {
            var response = await requestEngine.ExecuteRequestAsync(function, parameters, responseValidator).ConfigureAwait(false);

            return SetVersion(XmlDeserializer<T>.DeserializeList(response));
        }

        private T GetObject<T>(XmlFunction function, Parameters.Parameters parameters, Action<string> responseValidator = null)
        {
            var response = requestEngine.ExecuteRequest(function, new Parameters.Parameters(), responseValidator);

            return XmlDeserializer<T>.DeserializeType(response);
        }

        private async Task<T> GetObjectAsync<T>(XmlFunction function, Parameters.Parameters parameters)
        {
            var response = await requestEngine.ExecuteRequestAsync(function, parameters).ConfigureAwait(false);

            return XmlDeserializer<T>.DeserializeType(response);
        }

        private T GetObject<T>(JsonFunction function, Parameters.Parameters parameters, Func<HttpResponseMessage, string> responseParser = null)
        {
            var response = requestEngine.ExecuteRequest(function, parameters, responseParser);

            var data = JsonDeserializer<T>.DeserializeType(response);

            return data;
        }

        private async Task<T> GetObjectAsync<T>(JsonFunction function, Parameters.Parameters parameters, Func<HttpResponseMessage, Task<string>> responseParser = null)
        {
            var response = await requestEngine.ExecuteRequestAsync(function, parameters, responseParser).ConfigureAwait(false);

            var data = JsonDeserializer<T>.DeserializeType(response);

            return data;
        }

        private XmlDeserializer<T> SetVersion<T>(XmlDeserializer<T> data)
        {
            if (version == null)
                version = Version.Parse(data.Version.Trim('+'));

            return data;
        }

        #endregion
        #region Get Objects XML

        private XDocument GetObjectsXml(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null) =>
            requestEngine.ExecuteRequest(function, parameters, responseValidator);

        private async Task<XDocument> GetObjectsXmlAsync(Parameters.Parameters parameters, XmlFunction function = XmlFunction.TableData, Action<string> responseValidator = null) =>
            await requestEngine.ExecuteRequestAsync(function, parameters, responseValidator).ConfigureAwait(false);

        #endregion
        #region Stream Objects

        private IEnumerable<T> StreamObjects<T>(ContentParameters<T> parameters, bool serial)
        {
            return StreamObjects<T, ContentParameters<T>>(parameters, serial, () => GetTotalObjects(parameters.Content), null, null);
        }

        private IEnumerable<TObject> StreamObjects<TObject, TParam>(TParam parameters, bool serial, Func<int> getCount,
            Func<TParam, Task<List<TObject>>> getObjectsAsync,
            Func<TParam, List<TObject>> getObjects) where TParam : PageableParameters
        {
            Log("Preparing to stream objects");
            Log("Requesting total number of objects");

            var totalObjects = getCount();

            var limit = 20000;

            if (totalObjects > limit || serial)
            {
                if(totalObjects > limit)
                    Log($"Switching to serial stream mode as over {limit} objects were detected");

                return SerialStreamObjectsInternal(parameters, totalObjects, false, getObjects);
            }

            return StreamObjectsInternal(parameters, totalObjects, false, getObjectsAsync);
        }

        internal IEnumerable<TObject> StreamObjectsInternal<TObject, TParam>(TParam parameters, int totalObjects, bool directCall,
            Func<TParam, Task<List<TObject>>> getObjectsAsync = null) where TParam : PageableParameters
        {
            if (directCall)
                Log("Preparing to stream objects");

            if (getObjectsAsync == null)
                getObjectsAsync = p => GetObjectsAsync<TObject>(p);

            var tasks = new List<Task<List<TObject>>>();

            parameters.Count = 500;

            for (int i = 0; i < totalObjects;)
            {
                tasks.Add(getObjectsAsync(parameters));

                i = i + parameters.Count.Value;
                parameters.Page++;

                if (totalObjects - i < parameters.Count)
                    parameters.Count = totalObjects - i;
            }

            Log($"Requesting {totalObjects} objects from PRTG over {tasks.Count} tasks");

            var result = new ParallelObjectGenerator<List<TObject>>(tasks.WhenAnyForAll()).SelectMany(m => m);

            return result;
        }

        internal IEnumerable<TObject> SerialStreamObjectsInternal<TObject, TParam>(TParam parameters, int totalObjects, bool directCall,
            Func<TParam, List<TObject>> getObjects = null) where TParam : PageableParameters
        {
            if (directCall)
                Log("Preparing to serially stream objects");

            if (getObjects == null)
                getObjects = p => GetObjects<TObject>(p);

            parameters.Count = 500;

            for (int i = 0; i < totalObjects;)
            {
                var response = getObjects(parameters);

                //Some object types (such as Logs) lie about their total number of objects.
                //If no objects are returned, we've reached the total number of items
                if (response.Count == 0)
                    break;

                foreach (var obj in response)
                    yield return obj;

                i = i + parameters.Count.Value;
                parameters.Page++;

                if (totalObjects - i < parameters.Count)
                    parameters.Count = totalObjects - i;
            }
        }

        #endregion

        /// <summary>
        /// Apply a modification function to each element of a response.
        /// </summary>
        /// <typeparam name="T">The type of objects returned by the response.</typeparam>
        /// <param name="objects">The collection of objects to amend.</param>
        /// <param name="action">A modification function to apply to each element of the collection.</param>
        /// <returns>A collection of modified objects.</returns>
        internal List<T> Amend<T>(List<T> objects, Action<T> action)
        {
            foreach (var obj in objects)
            {
                action(obj);
            }

            return objects;
        }

        /// <summary>
        /// Apply a modification function to the properties of an object.
        /// </summary>
        /// <typeparam name="T">The type of object returned by the response.</typeparam>
        /// <param name="obj">The object to amend.</param>
        /// <param name="action">A modification function to apply to the object.</param>
        /// <returns></returns>
        [ExcludeFromCodeCoverage]
        internal T Amend<T>(T obj, Action<T> action)
        {
            action(obj);

            return obj;
        }

        /// <summary>
        /// Apply a modification action to a response, transforming the response to another type.
        /// </summary>
        /// <typeparam name="TSource">The type of object to transform.</typeparam>
        /// <typeparam name="TRet">The type of object to return.</typeparam>
        /// <param name="obj">The object to transform.</param>
        /// <param name="action">A modification function that transforms the response from one type to another.</param>
        /// <returns></returns>
        internal TRet Amend<TSource, TRet>(TSource obj, Func<TSource, TRet> action)
        {
            var val = action(obj);

            return val;
        }

        #region Sensors
            #region Default

        /// <summary>
        /// Retrieve all sensors from a PRTG Server.
        /// </summary>
        /// <returns>A list of all sensors on a PRTG Server.</returns>
        public List<Sensor> GetSensors() => GetSensors(new SensorParameters());

        /// <summary>
        /// Asynchronously retrieve all sensors from a PRTG Server.
        /// </summary>
        /// <returns>A task that returns a list of all sensors on a PRTG Server.</returns>
        public async Task<List<Sensor>> GetSensorsAsync() => await GetSensorsAsync(new SensorParameters()).ConfigureAwait(false);

        /// <summary>
        /// Stream all sensors from a PRTG Server. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.<para/>
        /// </summary>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Sensor> StreamSensors(bool serial = false) => StreamSensors(new SensorParameters(), serial);

            #endregion
            #region Sensor Status
   
        /// <summary>
        /// Retrieve sensors from a PRTG Server of one or more statuses.
        /// </summary>
        /// <param name="statuses">A list of sensor statuses to filter for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public List<Sensor> GetSensors(params Status[] statuses) => GetSensors(new SensorParameters { Status = statuses });

        /// <summary>
        /// Asynchronously retrieve sensors from a PRTG Server of one or more statuses.
        /// </summary>
        /// <param name="statuses">A list of sensor statuses to filter for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public async Task<List<Sensor>> GetSensorsAsync(params Status[] statuses) => await GetSensorsAsync(new SensorParameters { Status = statuses }).ConfigureAwait(false);

        /// <summary>
        /// Stream sensors from a PRTG Server of one or more statuses. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="statuses">A list of sensor statuses to filter for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Sensor> StreamSensors(params Status[] statuses) => StreamSensors(new SensorParameters { Status = statuses });

            #endregion
            #region Filter (Property, Value)

        /// <summary>
        /// Retrieve sensors from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public List<Sensor> GetSensors(Property property, object value) => GetSensors(new SearchFilter(property, value));

        /// <summary>
        /// Asynchronously retrieve sensors from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public async Task<List<Sensor>> GetSensorsAsync(Property property, object value) => await GetSensorsAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream sensors from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Sensor> StreamSensors(Property property, object value) => StreamSensors(new SearchFilter(property, value));

            #endregion
            #region Filter (Property, Operator, Value)

        /// <summary>
        /// Retrieve sensors from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public List<Sensor> GetSensors(Property property, FilterOperator @operator, object value) => GetSensors(new SearchFilter(property, @operator, value));

        /// <summary>
        /// Asynchronously retrieve sensors from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public async Task<List<Sensor>> GetSensorsAsync(Property property, FilterOperator @operator, object value) => await GetSensorsAsync(new SearchFilter(property, @operator, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream sensors from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Sensor> StreamSensors(Property property, FilterOperator @operator, object value) => StreamSensors(new SearchFilter(property, @operator, value));

            #endregion
            #region Filter (Array)

        /// <summary>
        /// Retrieve sensors from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public List<Sensor> GetSensors(params SearchFilter[] filters) => GetSensors(new SensorParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve sensors from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of sensors that match the specified search criteria.</returns>
        public async Task<List<Sensor>> GetSensorsAsync(params SearchFilter[] filters) => await GetSensorsAsync(new SensorParameters { SearchFilter = filters }).ConfigureAwait(false);

        /// <summary>
        /// Stream sensors from a PRTG Server based on the values of multiple properties. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Sensor> StreamSensors(params SearchFilter[] filters) => StreamSensors(new SensorParameters { SearchFilter = filters });

            #endregion
            #region Parameters

        /// <summary>
        /// Retrieve sensors from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Sensors.</param>
        /// <returns>A list of sensors that match the specified parameters.</returns>
        public List<Sensor> GetSensors(SensorParameters parameters) => GetObjects<Sensor>(parameters);

        /// <summary>
        /// Asynchronously retrieve sensors from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Sensors.</param>
        /// <returns>A list of sensors that match the specified parameters.</returns>
        public async Task<List<Sensor>> GetSensorsAsync(SensorParameters parameters) => await GetObjectsAsync<Sensor>(parameters).ConfigureAwait(false);

        /// <summary>
        /// Stream sensors from a PRTG Server using a custom set of parameters. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Sensors.</param>
        /// <param name="serial">Specifies whether PrtgAPI should execute all requests one at a time rather than all at once.</param>
        /// <returns>A list of sensors that match the specified parameters.</returns>
        public IEnumerable<Sensor> StreamSensors(SensorParameters parameters, bool serial = false) => StreamObjects(parameters, serial);

            #endregion

        /// <summary>
        /// Retrieves descriptions of all sensor types that can be created under a specified object. Actual supported types may differ based on current PRTG settings.<para/>
        /// If the specified object does not support querying sensor types, this method returns null.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve supported types of.</param>
        /// <returns>If the specified object supports querying sensor types, a list descriptions of sensor types supported by the specified object. Otherwise, null.</returns>
        public List<SensorTypeDescriptor> GetSensorTypes(int objectId = 1) =>
            ResponseParser.ParseSensorTypes(GetObject<SensorTypeDescriptorInternal>(JsonFunction.SensorTypes, new BaseActionParameters(objectId), ResponseParser.ValidateHasContent).Types);

        /// <summary>
        /// Asynchronously retrieves descriptions of all sensor types that can be created under a specified object. Actual supported types may differ based on current PRTG settings.<para/>
        /// If the specified object does not support querying sensor types, this method returns null.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve supported types of.</param>
        /// <returns>If the specified object supports querying sensor types, a list descriptions of sensor types supported by the specified object. Otherwise, null.</returns>
        public async Task<List<SensorTypeDescriptor>> GetSensorTypesAsync(int objectId = 1) =>
            ResponseParser.ParseSensorTypes((await GetObjectAsync<SensorTypeDescriptorInternal>(JsonFunction.SensorTypes, new BaseActionParameters(objectId), ResponseParser.ValidateHasContentAsync).ConfigureAwait(false)).Types);

        /// <summary>
        /// Retrieve the number of sensors of each sensor type in the system.
        /// </summary>
        /// <returns>The total number of sensors of each <see cref="Status"/> type.</returns>
        public SensorTotals GetSensorTotals() =>
            GetObject<SensorTotals>(XmlFunction.GetTreeNodeStats, new Parameters.Parameters());

        /// <summary>
        /// Asynchronously retrieve the number of sensors of each sensor type in the system.
        /// </summary>
        /// <returns>The total number of sensors of each <see cref="Status"/> type.</returns>
        public async Task<SensorTotals> GetSensorTotalsAsync() =>
            await GetObjectAsync<SensorTotals>(XmlFunction.GetTreeNodeStats, new Parameters.Parameters()).ConfigureAwait(false);

        #endregion
        #region Devices
            #region Default

        /// <summary>
        /// Retrieve all devices from a PRTG Server.
        /// </summary>
        /// <returns>A list of all devices on a PRTG Server.</returns>
        public List<Device> GetDevices() => GetDevices(new DeviceParameters());

        /// <summary>
        /// Asynchronously retrieve all devices from a PRTG Server.
        /// </summary>
        /// <returns>A list of all devices on a PRTG Server.</returns>
        public async Task<List<Device>> GetDevicesAsync() => await GetDevicesAsync(new DeviceParameters()).ConfigureAwait(false);

        /// <summary>
        /// Stream all devices from a PRTG Server. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Device> StreamDevices(bool serial = false) => StreamDevices(new DeviceParameters(), serial);

            #endregion
            #region Filter (Property, Value)

        /// <summary>
        /// Retrieve devices from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public List<Device> GetDevices(Property property, object value) => GetDevices(new SearchFilter(property, value));

        /// <summary>
        /// Asynchronously retrieve devices from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public async Task<List<Device>> GetDevicesAsync(Property property, object value) => await GetDevicesAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream devices from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Device> StreamDevices(Property property, object value) => StreamDevices(new SearchFilter(property, value));

            #endregion
            #region Filter (Property, Operator, Value)

        /// <summary>
        /// Retrieve devices from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public List<Device> GetDevices(Property property, FilterOperator @operator, string value) => GetDevices(new SearchFilter(property, @operator, value));

        /// <summary>
        /// Asynchronously retrieve devices from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public async Task<List<Device>> GetDevicesAsync(Property property, FilterOperator @operator, string value) => await GetDevicesAsync(new SearchFilter(property, @operator, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream devices from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Device> StreamDevices(Property property, FilterOperator @operator, string value) => StreamDevices(new SearchFilter(property, @operator, value));

            #endregion
            #region Filter (Array)

        /// <summary>
        /// Retrieve devices from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public List<Device> GetDevices(params SearchFilter[] filters) => GetDevices(new DeviceParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve devices from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of devices that match the specified search criteria.</returns>
        public async Task<List<Device>> GetDevicesAsync(params SearchFilter[] filters) => await GetDevicesAsync(new DeviceParameters { SearchFilter = filters }).ConfigureAwait(false);

        /// <summary>
        /// Stream devices from a PRTG Server based on the values of multiple properties. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Device> StreamDevices(params SearchFilter[] filters) => StreamDevices(new DeviceParameters { SearchFilter = filters });

            #endregion
            #region Parameters

        /// <summary>
        /// Retrieve devices from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Devices.</param>
        /// <returns>A list of devices that match the specified parameters.</returns>
        public List<Device> GetDevices(DeviceParameters parameters) => GetObjects<Device>(parameters);

        /// <summary>
        /// Asynchronously retrieve devices from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Devices.</param>
        /// <returns>A list of devices that match the specified parameters.</returns>
        public async Task<List<Device>> GetDevicesAsync(DeviceParameters parameters) => await GetObjectsAsync<Device>(parameters).ConfigureAwait(false);

        /// <summary>
        /// Stream devices from a PRTG Server using a custom set of parameters. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Devices.</param>
        /// <param name="serial">Specifies whether PrtgAPI should execute all requests one at a time rather than all at once.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Device> StreamDevices(DeviceParameters parameters, bool serial = false) => StreamObjects(parameters, serial);

            #endregion

        /// <summary>
        /// Retrieves all auto-discovery device templates supported by the specified object.
        /// </summary>
        /// <param name="deviceId">The ID of the device to retrieve supported device templates of. In practice all devices should support the same device templates.</param>
        /// <returns>A list of device templates supported by the specified object.</returns>
        public List<DeviceTemplate> GetDeviceTemplates(int deviceId = 40) =>
            ResponseParser.GetTemplates(GetObjectPropertiesRawInternal(deviceId, ObjectType.Device));

        /// <summary>
        /// Asynchronously retrieves all auto-discovery device templates supported by the specified object.
        /// </summary>
        /// <param name="deviceId">The ID of the device to retrieve supported device templates of. In practice all devices should support the same device templates.</param>
        /// <returns>A list of device templates supported by the specified object.</returns>
        public async Task<List<DeviceTemplate>> GetDeviceTemplatesAsync(int deviceId = 40) =>
            ResponseParser.GetTemplates(await GetObjectPropertiesRawInternalAsync(deviceId, ObjectType.Device).ConfigureAwait(false));

        #endregion
        #region Groups
            #region Default

        /// <summary>
        /// Retrieve all groups from a PRTG Server.
        /// </summary>
        /// <returns>A list of all groups on a PRTG Server.</returns>
        public List<Group> GetGroups() => GetGroups(new GroupParameters());

        /// <summary>
        /// Asynchronously retrieve all groups from a PRTG Server.
        /// </summary>
        /// <returns>A list of all groups on a PRTG Server.</returns>
        public async Task<List<Group>> GetGroupsAsync() => await GetGroupsAsync(new GroupParameters()).ConfigureAwait(false);

        /// <summary>
        /// Stream all groups from a PRTG Server. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Group> StreamGroups(bool serial = false) => StreamGroups(new GroupParameters(), serial);

            #endregion
            #region Filter (Property, Value)

        /// <summary>
        /// Retrieve groups from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public List<Group> GetGroups(Property property, object value) => GetGroups(new SearchFilter(property, value));

        /// <summary>
        /// Asynchronously retrieve groups from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public async Task<List<Group>> GetGroupsAsync(Property property, object value) => await GetGroupsAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream groups from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Group> StreamGroups(Property property, object value) => StreamGroups(new SearchFilter(property, value));

            #endregion
            #region Filter (Property, Operator, Group)

        /// <summary>
        /// Retrieve groups from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public List<Group> GetGroups(Property property, FilterOperator @operator, string value) => GetGroups(new SearchFilter(property, @operator, value));

        /// <summary>
        /// Asynchronously retrieve groups from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public async Task<List<Group>> GetGroupsAsync(Property property, FilterOperator @operator, string value) => await GetGroupsAsync(new SearchFilter(property, @operator, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream groups from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Group> StreamGroups(Property property, FilterOperator @operator, string value) => StreamGroups(new SearchFilter(property, @operator, value));

            #endregion
            #region Filter (Array)

        /// <summary>
        /// Retrieve groups from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public List<Group> GetGroups(params SearchFilter[] filters) => GetGroups(new GroupParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve groups from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of groups that match the specified search criteria.</returns>
        public async Task<List<Group>> GetGroupsAsync(params SearchFilter[] filters) => await GetGroupsAsync(new GroupParameters { SearchFilter = filters }).ConfigureAwait(false);

        /// <summary>
        /// Stream groups from a PRTG Server based on the values of multiple properties. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Group> StreamGroups(params SearchFilter[] filters) => StreamGroups(new GroupParameters { SearchFilter = filters });

            #endregion
            #region Parameters

        /// <summary>
        /// Retrieve groups from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Groups.</param>
        /// <returns>A list of groups that match the specified parameters.</returns>
        public List<Group> GetGroups(GroupParameters parameters) => GetObjects<Group>(parameters);

        /// <summary>
        /// Asynchronously retrieve groups from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <returns>A list of groups that match the specified parameters.</returns>
        public async Task<List<Group>> GetGroupsAsync(GroupParameters parameters) => await GetObjectsAsync<Group>(parameters).ConfigureAwait(false);

        /// <summary>
        /// Stream groups from a PRTG Server using a custom set of parameters. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Group> StreamGroups(GroupParameters parameters, bool serial = false) => StreamObjects(parameters, serial);

            #endregion
        #endregion
        #region Probes
            #region Default

        /// <summary>
        /// Retrieve all probes from a PRTG Server.
        /// </summary>
        /// <returns>A list of all probes on a PRTG Server.</returns>
        public List<Probe> GetProbes() => GetProbes(new ProbeParameters());

        /// <summary>
        /// Asynchronously retrieve all probes from a PRTG Server.
        /// </summary>
        /// <returns>A list of all probes on a PRTG Server.</returns>
        public async Task<List<Probe>> GetProbesAsync() => await GetProbesAsync(new ProbeParameters()).ConfigureAwait(false);

        /// <summary>
        /// Stream all probes from a PRTG Server. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Probe> StreamProbes(bool serial = false) => StreamProbes(new ProbeParameters(), serial);

            #endregion
            #region Filter (Property, Value)

        /// <summary>
        /// Retrieve probes from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public List<Probe> GetProbes(Property property, object value) => GetProbes(new SearchFilter(property, value));

        /// <summary>
        /// Asynchronously retrieve probes from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public async Task<List<Probe>> GetProbesAsync(Property property, object value) => await GetProbesAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream probes from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Probe> StreamProbes(Property property, object value) => StreamProbes(new SearchFilter(property, value));

            #endregion
            #region Filter (Property, Operator, Value)

        /// <summary>
        /// Retrieve probes from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public List<Probe> GetProbes(Property property, FilterOperator @operator, string value) => GetProbes(new SearchFilter(property, @operator, value));

        /// <summary>
        /// Asynchronously retrieve probes from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public async Task<List<Probe>> GetProbesAsync(Property property, FilterOperator @operator, string value) => await GetProbesAsync(new SearchFilter(property, @operator, value)).ConfigureAwait(false);

        /// <summary>
        /// Stream probes from a PRTG Server based on the value of a certain property. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="operator">Operator to compare value and property value with.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Probe> StreamProbes(Property property, FilterOperator @operator, string value) => StreamProbes(new SearchFilter(property, @operator, value));

            #endregion
            #region Filter (Array)

        /// <summary>
        /// Retrieve probes from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public List<Probe> GetProbes(params SearchFilter[] filters) => GetProbes(new ProbeParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve probes from a PRTG Server based on the values of multiple properties.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of probes that match the specified search criteria.</returns>
        public async Task<List<Probe>> GetProbesAsync(params SearchFilter[] filters) => await GetProbesAsync(new ProbeParameters { SearchFilter = filters }).ConfigureAwait(false);

        /// <summary>
        /// Stream probes from a PRTG Server based on the values of multiple properties. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Probe> StreamProbes(params SearchFilter[] filters) => StreamProbes(new ProbeParameters { SearchFilter = filters });

            #endregion
            #region Parameters

        /// <summary>
        /// Retrieve probes from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Probes.</param>
        /// <returns>A list of probes that match the specified parameters.</returns>
        public List<Probe> GetProbes(ProbeParameters parameters) => GetObjects<Probe>(parameters);

        /// <summary>
        /// Asynchronously retrieve probes from a PRTG Server using a custom set of parameters.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Probes.</param>
        /// <returns>A list of probes that match the specified parameters.</returns>
        public async Task<List<Probe>> GetProbesAsync(ProbeParameters parameters) => await GetObjectsAsync<Probe>(parameters).ConfigureAwait(false);

        /// <summary>
        /// Stream probes from a PRTG Server using a custom set of parameters. When this method's response is enumerated multiple parallel requests will be executed against the PRTG Server and yielded in the order they return.
        /// </summary>
        /// <param name="parameters">A custom set of parameters used to retrieve PRTG Probes.</param>
        /// <param name="serial">Specifies whether PrtgAPI should execute all requests one at a time rather than all at once.</param>
        /// <returns>A generator encapsulating a series of <see cref="Task"/> objects capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<Probe> StreamProbes(ProbeParameters parameters, bool serial = false) => StreamObjects(parameters, serial);

            #endregion
        #endregion
        #region Channel

        /// <summary>
        /// Retrieve all channels of a sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve channels for.</param>
        /// <returns></returns>
        public List<Channel> GetChannels(int sensorId) => GetChannelsInternal(sensorId);

        /// <summary>
        /// Retrieve all channels of a sensor that match the specified name.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve channels for.</param>
        /// <param name="channelName">The name of the channel to retrieve.</param>
        /// <returns></returns>
        public List<Channel> GetChannels(int sensorId, string channelName) => GetChannelsInternal(sensorId, name => name == channelName);

        /// <summary>
        /// Asynchronously retrieve all channels of a sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve channels for.</param>
        /// <returns></returns>
        public async Task<List<Channel>> GetChannelsAsync(int sensorId) => await GetChannelsInternalAsync(sensorId).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously retrieve all channels of a sensor that match the specified name.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve channels for.</param>
        /// <param name="channelName">The name of the channel to retrieve.</param>
        /// <returns></returns>
        public async Task<List<Channel>> GetChannelsAsync(int sensorId, string channelName) => await GetChannelsInternalAsync(sensorId, name => name == channelName).ConfigureAwait(false);

        private XElement GetChannelProperties(int sensorId, int channelId)
        {
            var parameters = new ChannelPropertiesParameters(sensorId, channelId);

            return requestEngine.ExecuteRequest(HtmlFunction.ChannelEdit, parameters, r => ChannelSettings.GetChannelXml(r, channelId));
        }

        private async Task<XElement> GetChannelPropertiesAsync(int sensorId, int channelId)
        {
            var parameters = new ChannelPropertiesParameters(sensorId, channelId);

            return await requestEngine.ExecuteRequestAsync(HtmlFunction.ChannelEdit, parameters, r => ChannelSettings.GetChannelXml(r, channelId)).ConfigureAwait(false);
        }

        #endregion
        #region Logs
            #region DateTime

        /// <summary>
        /// Retrieve logs between two time periods from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="startDate">Start date to retrieve logs from. If this value is null, logs will be retrieved from the current date and time.</param>
        /// <param name="endDate">End date to retrieve logs to. If this value is null, logs will be retrieved until the beginning of all logs.</param>
        /// <param name="count">Number of logs to retrieve. Depending on the number of logs stored in the system, specifying a high number may cause the request to timeout.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public List<Log> GetLogs(int? objectId, DateTime? startDate = null, DateTime? endDate = null, int count = 500, params LogStatus[] status) =>
            GetObjects<Log>(new LogParameters(objectId, startDate, endDate, count, status));

        /// <summary>
        /// Asynchronously retrieve logs between two time periods from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="startDate">Start date to retrieve logs from. If this value is null, logs will be retrieved from the current date and time.</param>
        /// <param name="endDate">End date to retrieve logs to. If this value is null, logs will be retrieved until the beginning of all logs.</param>
        /// <param name="count">Number of logs to retrieve. Depending on the number of logs stored in the system, specifying a high number may cause the request to timeout.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public async Task<List<Log>> GetLogsAsync(int? objectId, DateTime? startDate = null, DateTime? endDate = null, int count = 500, params LogStatus[] status) =>
            await GetObjectsAsync<Log>(new LogParameters(objectId, startDate, endDate, count, status)).ConfigureAwait(false);

        /// <summary>
        /// Stream logs between two time periods from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="startDate">Start date to retrieve logs from. If this value is null, logs will be retrieved from the current date and time.</param>
        /// <param name="endDate">End date to retrieve logs to. If this value is null, logs will be retrieved until the beginning of all logs.</param>
        /// <param name="serial">Specifies whether PrtgAPI should execute all requests one at a time rather than all at once.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public IEnumerable<Log> StreamLogs(int? objectId, DateTime? startDate = null, DateTime? endDate = null, bool serial = false, params LogStatus[] status) =>
            StreamObjects(new LogParameters(objectId, startDate, endDate, status: status), serial);

            #endregion
            #region RecordAge
        
        /// <summary>
        /// Retrieve logs from a standard time period from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="timeSpan">Time period to retrieve logs from. Logs will be retrieved from the beginning of this period until the current date and time, ordered newest to oldest.</param>
        /// <param name="count">Number of logs to retrieve. Depending on the number of logs stored in the system, specifying a high number may cause the request to timeout.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public List<Log> GetLogs(int? objectId = null, RecordAge timeSpan = RecordAge.LastWeek, int count = 500, params LogStatus[] status) =>
            GetObjects<Log>(new LogParameters(objectId, timeSpan, count, status));

        /// <summary>
        /// Asynchronously retrieve logs from a standard time period from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="timeSpan">Time period to retrieve logs from. Logs will be retrieved from the beginning of this period until the current date and time, ordered newest to oldest.</param>
        /// <param name="count">Number of logs to retrieve. Depending on the number of logs stored in the system, specifying a high number may cause the request to timeout.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public async Task<List<Log>> GetLogsAsync(int? objectId = null, RecordAge timeSpan = RecordAge.LastWeek, int count = 500, params LogStatus[] status) =>
            await GetObjectsAsync<Log>(new LogParameters(objectId, timeSpan, count, status)).ConfigureAwait(false);

        /// <summary>
        /// Stream logs from a standard time period from a PRTG Server. Logs are ordered from newest to oldest.
        /// </summary>
        /// <param name="objectId">ID of the object to retrieve logs from. If this value is null or 0, logs will be retrieved from the root group.</param>
        /// <param name="timeSpan">Time period to retrieve logs from. Logs will be retrieved from the beginning of this period until the current date and time, ordered newest to oldest.</param>
        /// <param name="serial">Specifies whether PrtgAPI should execute all requests one at a time rather than all at once.</param>
        /// <param name="status">Log event types to retrieve records for. If no types are specified, all record types will be retrieved.</param>
        /// <returns>All logs that meet the specified criteria.</returns>
        public IEnumerable<Log> StreamLogs(int? objectId = null, RecordAge timeSpan = RecordAge.LastWeek, bool serial = false, params LogStatus[] status) =>
            StreamObjects(new LogParameters(objectId, timeSpan, status: status), serial);

            #endregion
        #endregion
        #region Notification Actions

        /// <summary>
        /// Retrieve notification actions from a PRTG Server.
        /// </summary>
        /// <returns>A list of all notification actions present on a PRTG Server.</returns>
        public List<NotificationAction> GetNotificationActions() =>
            GetNotificationActionsInternal(new NotificationActionParameters());

        /// <summary>
        /// Retrieve notification actions from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>All notification actions whose property matched the specified value.</returns>
        public List<NotificationAction> GetNotificationActions(Property property, object value) =>
            GetNotificationActions(new SearchFilter(property, value));

        /// <summary>
        /// Retrieve all notification actions on a PRTG Server, filtering for objects by one or more conditions.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>All objects that match the specified conditions.</returns>
        public List<NotificationAction> GetNotificationActions(params SearchFilter[] filters) =>
            GetNotificationActionsInternal(new NotificationActionParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve notification actions from a PRTG Server.
        /// </summary>
        /// <returns>A list of all notification actions present on a PRTG Server.</returns>
        public async Task<List<NotificationAction>> GetNotificationActionsAsync() =>
            await GetNotificationActionsInternalAsync(new NotificationActionParameters()).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously retrieve notification actions from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>All notification actions whose property matched the specified value.</returns>
        public async Task<List<NotificationAction>> GetNotificationActionsAsync(Property property, object value) =>
            await GetNotificationActionsAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously retrieve all notification actions on a PRTG Server, filtering for objects by one or more conditions.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>All objects that match the specified conditions.</returns>
        public async Task<List<NotificationAction>> GetNotificationActionsAsync(params SearchFilter[] filters) =>
            await GetNotificationActionsInternalAsync(new NotificationActionParameters { SearchFilter = filters }).ConfigureAwait(false);

        private XElement GetNotificationActionProperties(int id)
        {
            var xml = requestEngine.ExecuteRequest(HtmlFunction.EditNotification, new BaseActionParameters(id), ObjectSettings.GetXml);

            xml = ResponseParser.GroupNotificationActionProperties(xml);

            return xml;
        }

        private async Task<XElement> GetNotificationActionPropertiesAsync(int id)
        {
            var xml = await requestEngine.ExecuteRequestAsync(HtmlFunction.EditNotification, new BaseActionParameters(id), ObjectSettings.GetXml).ConfigureAwait(false);

            xml = ResponseParser.GroupNotificationActionProperties(xml);

            return xml;
        }

        #endregion
        #region Notification Triggers

        /// <summary>
        /// Retrieve all notification triggers of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The object to retrieve triggers for.</param>
        /// <returns>A list of notification triggers that apply to the specified object.</returns>
        public List<NotificationTrigger> GetNotificationTriggers(int objectId)
        {
            var xmlResponse = requestEngine.ExecuteRequest(XmlFunction.TableData, new NotificationTriggerParameters(objectId));

            var parsed = ResponseParser.ParseNotificationTriggerResponse(objectId, xmlResponse);

            UpdateTriggerChannels(parsed);
            UpdateTriggerActions(parsed);

            return parsed;
        }

        /// <summary>
        /// Asynchronously retrieve all notification triggers of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The object to retrieve triggers for.</param>
        /// <returns>A list of notification triggers that apply to the specified object.</returns>
        public async Task<List<NotificationTrigger>> GetNotificationTriggersAsync(int objectId)
        {
            var xmlResponse = await requestEngine.ExecuteRequestAsync(XmlFunction.TableData, new NotificationTriggerParameters(objectId)).ConfigureAwait(false);

            var parsed = ResponseParser.ParseNotificationTriggerResponse(objectId, xmlResponse);

            await UpdateTriggerChannelsAsync(parsed).ConfigureAwait(false);
            await UpdateTriggerActionsAsync(parsed).ConfigureAwait(false);

            return parsed;
        }

        private void UpdateTriggerActions(List<NotificationTrigger> triggers)
        {
            var actions = ResponseParser.GroupTriggerActions(triggers);

            var parameters = new NotificationActionParameters(actions.Select(a => a.Key).ToArray());

            var normal = new Lazy<XDocument>(() => GetObjectsXml(parameters));

            foreach (var group in actions)
            {
                var lazy = new Lazy<XDocument>(() => RequestParser.ExtractActionXml(normal.Value, GetNotificationActionProperties(group.Key), @group.Key));

                foreach (var action in group)
                    action.LazyXml = lazy;
            }
        }

        private async Task UpdateTriggerActionsAsync(List<NotificationTrigger> triggers)
        {
            var actions = ResponseParser.GroupTriggerActions(triggers);

            var parameters = new NotificationActionParameters(actions.Select(a => a.Key).ToArray());

            var tasks = actions.Select(g => GetNotificationActionPropertiesAsync(g.Key));
            var normal = await GetObjectsXmlAsync(parameters).ConfigureAwait(false);

            //All the properties of all desired notifications
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            //For each different notification action
            for (int i = 0; i < actions.Count; i++)
            {
                var xDoc = RequestParser.ExtractActionXml(normal, results[i], actions[i].Key);

                //Foreach notification action with the same ID
                foreach (var action in actions[i])
                {
                    action.LazyXml = new Lazy<XDocument>(() => xDoc);
                }
            }
        }

        /// <summary>
        /// Retrieve all notification trigger types supported by a PRTG Object.
        /// </summary>
        /// <param name="objectId">The object to retrieve supported trigger types for.</param>
        /// <returns>The trigger types supported by the object.</returns>
        public List<TriggerType> GetNotificationTriggerTypes(int objectId) =>
            GetNotificationTriggerData(objectId).SupportedTypes.ToList();

        /// <summary>
        /// Asynchronously retrieve all notification trigger types supported by a PRTG Object.
        /// </summary>
        /// <param name="objectId">The object to retrieve supported trigger types for.</param>
        /// <returns>The trigger types supported by the object.</returns>
        public async Task<List<TriggerType>> GetNotificationTriggerTypesAsync(int objectId) =>
            (await GetNotificationTriggerDataAsync(objectId).ConfigureAwait(false)).SupportedTypes.ToList();

        private NotificationTriggerData GetNotificationTriggerData(int objectId) =>
            GetObject<NotificationTriggerData>(
                JsonFunction.Triggers,
                new BaseActionParameters(objectId),
                ParseNotificationTriggerTypes
            );

        private async Task<NotificationTriggerData> GetNotificationTriggerDataAsync(int objectId) =>
            await GetObjectAsync<NotificationTriggerData>(
                JsonFunction.Triggers,
                new BaseActionParameters(objectId),
                ParseNotificationTriggerTypesAsync
            ).ConfigureAwait(false);

        #endregion
        #region Schedules

        /// <summary>
        /// Retrieve all monitoring schedules from a PRTG Server.
        /// </summary>
        /// <returns>A list of monitoring schedules supported by a PRTG Server.</returns>
        public List<Schedule> GetSchedules() =>
            GetObjects<Schedule>(new ScheduleParameters());

        /// <summary>
        /// Retrieve all monitoring schedules from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>All monitoring schedules whose value matched the specified property.</returns>
        public List<Schedule> GetSchedules(Property property, object value) =>
            GetSchedules(new SearchFilter(property, value));

        /// <summary>
        /// Retrieve all monitoring schedules from a PRTG Server, filtering for objects by one or more conditions.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of schedules that match the specified search criteria.</returns>
        public List<Schedule> GetSchedules(params SearchFilter[] filters) =>
            GetObjects<Schedule>(new ScheduleParameters { SearchFilter = filters });

        /// <summary>
        /// Asynchronously retrieve all monitoring schedules from a PRTG Server.
        /// </summary>
        /// <returns>A list of monitoring schedules supported by a PRTG Server.</returns>
        public async Task<List<Schedule>> GetSchedulesAsync() =>
            await GetObjectsAsync<Schedule>(new ScheduleParameters()).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously retrieve all monitoring schedules from a PRTG Server based on the value of a certain property.
        /// </summary>
        /// <param name="property">Property to search against.</param>
        /// <param name="value">Value to search for.</param>
        /// <returns>All monitoring schedules whose value matched the specified property.</returns>
        public async Task<List<Schedule>> GetSchedulesAsync(Property property, object value) =>
            await GetSchedulesAsync(new SearchFilter(property, value)).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously retrieve all monitoring schedules from a PRTG Server, filtering for objects by one or more conditions.
        /// </summary>
        /// <param name="filters">One or more filters used to limit search results.</param>
        /// <returns>A list of schedules that match the specified search criteria.</returns>
        public async Task<List<Schedule>> GetSchedulesAsync(params SearchFilter[] filters) =>
            await GetObjectsAsync<Schedule>(new ScheduleParameters { SearchFilter = filters }).ConfigureAwait(false);

        #endregion
    #endregion
    #region Object Manipulation
        #region Add Objects

        /// <summary>
        /// Add a new sensor to a PRTG device.
        /// </summary>
        /// <param name="deviceId">The ID of the device the sensor will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the sensor to create.
        /// Depending on the type of sensor parameters specified, this may result in the creation of several new sensors.</param>
        /// <param name="resolve">Whether to resolve the new sensors to their resultant <see cref="Sensor"/> objects.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, all new sensors that were created from the sensor <paramref name="parameters"/>. Otherwise, null.</returns>
        public List<Sensor> AddSensor(int deviceId, NewSensorParameters parameters, bool resolve = true) =>
            AddObject(deviceId, parameters, CommandFunction.AddSensor5, GetSensors, resolve, allowMultiple: true);

        /// <summary>
        /// Asynchronously add a new sensor to a PRTG device.
        /// </summary>
        /// <param name="deviceId">The ID of the device the sensor will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the sensor to create.
        /// Depending on the type of sensor parameters specified, this may result in the creation of several new sensors.</param>
        /// <param name="resolve">Whether to resolve the new sensors to their resultant <see cref="Sensor"/> objects.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, all new sensors that were created from the sensor <paramref name="parameters"/>. Otherwise, null.</returns>
        public async Task<List<Sensor>> AddSensorAsync(int deviceId, NewSensorParameters parameters, bool resolve = true) =>
            await AddObjectAsync(deviceId, parameters, CommandFunction.AddSensor5, GetSensorsAsync, resolve, allowMultiple: true).ConfigureAwait(false);

        /// <summary>
        /// Add a new device to a PRTG group or probe.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the device will apply to.</param>
        /// <param name="name">The name to use for the new device.</param>
        /// <param name="host">The hostname or IP Address PRTG should use to communicate with the device. If this value is null, the <paramref name="name"/> will be used.</param>
        /// <param name="discoveryMode">Whether an auto-discovery should be automatically performed after device creation.</param>
        /// <param name="resolve">Whether to resolve the new device to its resultant <see cref="Device"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the device that was created from this method's device parameters. Otherwise, null.</returns>
        public Device AddDevice(int parentId, string name, string host = null, AutoDiscoveryMode discoveryMode = AutoDiscoveryMode.Manual, bool resolve = true) =>
                AddDevice(parentId, new NewDeviceParameters(name, host) { AutoDiscoveryMode = discoveryMode }, resolve);

        /// <summary>
        /// Add a new device to a PRTG group or probe with a complex set of parameters.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the device will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the device to create.</param>
        /// <param name="resolve">Whether to resolve the new device to its resultant <see cref="Device"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the device that was created from this method's device <paramref name="parameters"/>. Otherwise, null.</returns>
        public Device AddDevice(int parentId, NewDeviceParameters parameters, bool resolve = true) =>
            AddObject(parentId, parameters, CommandFunction.AddDevice2, GetDevices, resolve)?.Single();

        /// <summary>
        /// Asynchronously add a new device to a PRTG group or probe.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the device will apply to.</param>
        /// <param name="name">The name to use for the new device.</param>
        /// <param name="host">The hostname or IP Address PRTG should use to communicate with the device. If this value is null, the <paramref name="name"/> will be used.</param>
        /// <param name="discoveryMode">Whether an auto-discovery should be automatically performed after device creation.</param>
        /// <param name="resolve">Whether to resolve the new device to its resultant <see cref="Device"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the device that was created from this method's device parameters. Otherwise, null.</returns>
        public async Task<Device> AddDeviceAsync(int parentId, string name, string host = null, AutoDiscoveryMode discoveryMode = AutoDiscoveryMode.Manual, bool resolve = true) =>
                await AddDeviceAsync(parentId, new NewDeviceParameters(name, host) { AutoDiscoveryMode = discoveryMode }, resolve).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously add a new device to a PRTG group or probe with a complex set of parameters.
        /// </summary>
        /// <param name="parentId">The ID of the group or device the device will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the device to create.</param>
        /// <param name="resolve">Whether to resolve the new device to its resultant <see cref="Device"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the device that was created from this method's device <paramref name="parameters"/>. Otherwise, null.</returns>
        public async Task<Device> AddDeviceAsync(int parentId, NewDeviceParameters parameters, bool resolve = true) =>
            (await AddObjectAsync(parentId, parameters, CommandFunction.AddDevice2, GetDevicesAsync, resolve).ConfigureAwait(false))?.Single();

        /// <summary>
        /// Add a new group to a PRTG group or probe.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the group will apply to.</param>
        /// <param name="name">The name to use for the new group.</param>
        /// <param name="resolve">Whether to resolve the new group to its resultant <see cref="Group"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the group that was created from this method's group parameters. Otherwise, null.</returns>
        public Group AddGroup(int parentId, string name, bool resolve = true) =>
            AddGroup(parentId, new NewGroupParameters(name), resolve);

        /// <summary>
        /// Add a new group to a PRTG group or probe with a complex set of parameters.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the group will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the group to create.</param>
        /// <param name="resolve">Whether to resolve the new group to its resultant <see cref="Group"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the group that was created from this method's group <paramref name="parameters"/>. Otherwise, null.</returns>
        public Group AddGroup(int parentId, NewGroupParameters parameters, bool resolve = true) =>
            AddObject(parentId, parameters, CommandFunction.AddGroup2, GetGroups, resolve)?.Single();

        /// <summary>
        /// Asynchronously add a new group to a PRTG group or probe.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the group will apply to.</param>
        /// <param name="name">The name to use for the new group.</param>
        /// <param name="resolve">Whether to resolve the new group to its resultant <see cref="Group"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the group that was created from this method's group parameters. Otherwise, null.</returns>
        public async Task<Group> AddGroupAsync(int parentId, string name, bool resolve = true) =>
            await AddGroupAsync(parentId, new NewGroupParameters(name), resolve).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously add a new group to a PRTG group or probe with a complex set of parameters.
        /// </summary>
        /// <param name="parentId">The ID of the group or probe the group will apply to.</param>
        /// <param name="parameters">A set of parameters describing the properties of the group to create.</param>
        /// <param name="resolve">Whether to resolve the new group to its resultant <see cref="Group"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the group that was created from this method's group <paramref name="parameters"/>. Otherwise, null.</returns>
        public async Task<Group> AddGroupAsync(int parentId, NewGroupParameters parameters, bool resolve = true) =>
            (await AddObjectAsync(parentId, parameters, CommandFunction.AddGroup2, GetGroupsAsync, resolve).ConfigureAwait(false))?.Single();

        /// <summary>
        /// Create a set of dynamic sensor parameters for creating a new sensor of a specified type.
        /// </summary>
        /// <param name="deviceId">The ID of a device that supports the specified sensor type.</param>
        /// <param name="sensorType">The type of sensor to create sensor paramters for.<para/>
        /// Note: sensor parameters cannot be created for types that require additional information
        /// to be added before interrogating the target device.</param>
        /// <param name="progressCallback">A callback function used to monitor the progress of the request. If this function returns false, the request is aborted and this method returns null.</param>
        /// <returns>A dynamic set of sensor parameters that store the the parameters required to create a sensor of a specified type.</returns>
        public DynamicSensorParameters GetDynamicSensorParameters(int deviceId, string sensorType, Func<int, bool> progressCallback = null) =>
            new DynamicSensorParameters(GetSensorTargetsResponse(deviceId, sensorType, progressCallback), sensorType);

        /// <summary>
        /// Asynchronously create a set of dynamic sensor parameters for creating a new sensor of a specified type.
        /// </summary>
        /// <param name="deviceId">The ID of a device that supports the specified sensor type.</param>
        /// <param name="sensorType">The type of sensor to create sensor paramters for.<para/>
        /// Note: sensor parameters cannot be created for types that require additional information
        /// to be added before interrogating the target device.</param>
        /// <param name="progressCallback">A callback function used to monitor the progress of the request. If this function returns false, the request is aborted and this method returns null.</param>
        /// <returns>A dynamic set of sensor parameters that store the the parameters required to create a sensor of a specified type.</returns>
        public async Task<DynamicSensorParameters> GetDynamicSensorParametersAsync(int deviceId, string sensorType, Func<int, bool> progressCallback = null) =>
            new DynamicSensorParameters(await GetSensorTargetsResponseAsync(deviceId, sensorType, progressCallback).ConfigureAwait(false), sensorType);

        #endregion
        #region Sensor State

        /// <summary>
        /// Mark a <see cref="Status.Down"/> sensor as <see cref="Status.DownAcknowledged"/>. If an acknowledged sensor returns to <see cref="Status.Up"/>, it will not be acknowledged when it goes down again.
        /// </summary>
        /// <param name="objectId">ID of the sensor to acknowledge.</param>
        /// <param name="duration">Duration (in minutes) to acknowledge the sensor for. If null, sensor will be acknowledged indefinitely.</param>
        /// <param name="message">Message to display on the acknowledged sensor.</param>
        public void AcknowledgeSensor(int objectId, int? duration = null, string message = null) =>
            AcknowledgeSensor(new[] {objectId}, duration, message);

        /// <summary>
        /// Mark one or more <see cref="Status.Down"/> sensors as <see cref="Status.DownAcknowledged"/>. If an acknowledged sensor returns to <see cref="Status.Up"/>, it will not be acknowledged when it goes down again.
        /// </summary>
        /// <param name="objectIds">IDs of the sensors to acknowledge.</param>
        /// <param name="duration">Duration (in minutes) to acknowledge the sensors for. If null, sensors will be acknowledged indefinitely.</param>
        /// <param name="message">Message to display on the acknowledged sensors.</param>
        public void AcknowledgeSensor(int[] objectIds, int? duration = null, string message = null) =>
            requestEngine.ExecuteRequest(CommandFunction.AcknowledgeAlarm, new AcknowledgeSensorParameters(objectIds, duration, message));

        /// <summary>
        /// Asynchronously mark a <see cref="Status.Down"/> sensor as <see cref="Status.DownAcknowledged"/>. If an acknowledged sensor returns to <see cref="Status.Up"/>, it will not be acknowledged when it goes down again.
        /// </summary>
        /// <param name="objectId">ID of the sensor to acknowledge.</param>
        /// <param name="duration">Duration (in minutes) to acknowledge the sensor for. If null, sensor will be paused indefinitely.</param>
        /// <param name="message">Message to display on the acknowledged sensor.</param>
        public async Task AcknowledgeSensorAsync(int objectId, int? duration = null, string message = null) =>
            await AcknowledgeSensorAsync(new[] {objectId}, duration, message).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously mark one or more <see cref="Status.Down"/> sensors as <see cref="Status.DownAcknowledged"/>. If an acknowledged sensor returns to <see cref="Status.Up"/>, it will not be acknowledged when it goes down again.
        /// </summary>
        /// <param name="objectIds">IDs of the sensors to acknowledge.</param>
        /// <param name="duration">Duration (in minutes) to acknowledge the sensors for. If null, sensors will be acknowledged indefinitely.</param>
        /// <param name="message">Message to display on the acknowledged sensors.</param>
        public async Task AcknowledgeSensorAsync(int[] objectIds, int? duration = null, string message = null) =>
            await requestEngine.ExecuteRequestAsync(CommandFunction.AcknowledgeAlarm, new AcknowledgeSensorParameters(objectIds, duration, message)).ConfigureAwait(false);

        /// <summary>
        /// Pause a PRTG Object.
        /// </summary>
        /// <param name="objectId">ID of the object to pause.</param>
        /// <param name="durationMinutes">Duration (in minutes) to pause the object for. If null, object will be paused indefinitely.</param>
        /// <param name="pauseMessage">Message to display on the paused object.</param>
        public void PauseObject(int objectId, int? durationMinutes = null, string pauseMessage = null) =>
            PauseObject(new[] {objectId}, durationMinutes, pauseMessage);

        /// <summary>
        /// Pause one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to pause.</param>
        /// <param name="durationMinutes">Duration (in minutes) to pause the object for. If null, object will be paused indefinitely.</param>
        /// <param name="pauseMessage">Message to display on the paused objects.</param>
        public void PauseObject(int[] objectIds, int? durationMinutes = null, string pauseMessage = null)
        {
            var parameters = new PauseRequestParameters(objectIds, durationMinutes, pauseMessage);

            requestEngine.ExecuteRequest(parameters.Function, parameters.Parameters);
        }

        /// <summary>
        /// Asynchronously pause a PRTG Object.
        /// </summary>
        /// <param name="objectId">ID of the object to pause.</param>
        /// <param name="durationMinutes">Duration (in minutes) to pause the object for. If null, object will be paused indefinitely.</param>
        /// <param name="pauseMessage">Message to display on the paused object.</param>
        public async Task PauseObjectAsync(int objectId, int? durationMinutes = null, string pauseMessage = null) =>
            await PauseObjectAsync(new[] {objectId}, durationMinutes, pauseMessage).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously pause one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to pause.</param>
        /// <param name="durationMinutes">Duration (in minutes) to pause the object for. If null, object will be paused indefinitely.</param>
        /// <param name="pauseMessage">Message to display on the paused objects.</param>
        public async Task PauseObjectAsync(int[] objectIds, int? durationMinutes = null, string pauseMessage = null)
        {
            var parameters = new PauseRequestParameters(objectIds, durationMinutes, pauseMessage);

            await requestEngine.ExecuteRequestAsync(parameters.Function, parameters.Parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Resume one or more PRTG Objects (including sensors, devices, groups and probes) from a Paused or Simulated Error state.
        /// </summary>
        /// <param name="objectId">IDs of the objects to resume.</param>
        public void ResumeObject(params int[] objectId) =>
            requestEngine.ExecuteRequest(CommandFunction.Pause, new PauseParameters(objectId, PauseAction.Resume));

        /// <summary>
        /// Asynchronously resume one or more PRTG Objects (including sensors, devices, groups and probes) from a Paused or Simulated Error state.
        /// </summary>
        /// <param name="objectId">ID of the object to resume.</param>
        public async Task ResumeObjectAsync(params int[] objectId) =>
            await requestEngine.ExecuteRequestAsync(CommandFunction.Pause, new PauseParameters(objectId, PauseAction.Resume)).ConfigureAwait(false);

        /// <summary>
        /// Simulate a <see cref="Status.Down"/> state for one or more sensors.
        /// </summary>
        /// <param name="sensorIds">IDs of the sensors to simulate an error for.</param>
        public void SimulateError(params int[] sensorIds) => requestEngine.ExecuteRequest(CommandFunction.Simulate, new SimulateErrorParameters(sensorIds));

        /// <summary>
        /// Asynchronously simulate a <see cref="Status.Down"/> state for one or more sensors.
        /// </summary>
        /// <param name="sensorIds">IDs of the sensors to simulate an error for.</param>
        public async Task SimulateErrorAsync(params int[] sensorIds) => await requestEngine.ExecuteRequestAsync(CommandFunction.Simulate, new SimulateErrorParameters(sensorIds)).ConfigureAwait(false);

        #endregion
        #region Notifications

        /// <summary>
        /// Add a notification trigger to a PRTG Server.
        /// </summary>
        /// <param name="parameters">A set of parameters describing the type of notification trigger to create and the object to apply it to.</param>
        /// <param name="resolve">Whether to resolve the new trigger to its resultant <see cref="NotificationTrigger"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the trigger that was created from this method's trigger parameters. Otherwise, null.</returns>
        public NotificationTrigger AddNotificationTrigger(TriggerParameters parameters, bool resolve = true) =>
            AddNotificationTriggerInternal(parameters, resolve)?.Single();

        /// <summary>
        /// Asynchronously add a notification trigger to a PRTG Server.
        /// </summary>
        /// <param name="parameters">A set of parameters describing the type of notification trigger to create and the object to apply it to.</param>
        /// <param name="resolve">Whether to resolve the new trigger to its resultant <see cref="NotificationTrigger"/> object.
        /// If this value is false, this method will return null.</param>
        /// <returns>If <paramref name="resolve"/> is true, the trigger that was created from this method's trigger parameters. Otherwise, null.</returns>
        public async Task<NotificationTrigger> AddNotificationTriggerAsync(TriggerParameters parameters, bool resolve = true) =>
            (await AddNotificationTriggerInternalAsync(parameters, resolve).ConfigureAwait(false))?.Single();

        /// <summary>
        /// Add or edit a notification trigger on a PRTG Server.
        /// </summary>
        /// <param name="parameters">A set of parameters describing the type of notification trigger and how to manipulate it.</param>
        public void SetNotificationTrigger(TriggerParameters parameters)
        {
            ValidateTriggerParameters(parameters);

            requestEngine.ExecuteRequest(HtmlFunction.EditSettings, parameters);
        }

        /// <summary>
        /// Asynchronously add or edit a notification trigger on a PRTG Server.
        /// </summary>
        /// <param name="parameters">A set of parameters describing the type of notification trigger and how to manipulate it.</param>
        public async Task SetNotificationTriggerAsync(TriggerParameters parameters)
        {
            await ValidateTriggerParametersAsync(parameters).ConfigureAwait(false);

            await requestEngine.ExecuteRequestAsync(HtmlFunction.EditSettings, parameters).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a notification trigger from an object.
        /// </summary>
        /// <param name="trigger">The notification trigger to remove.</param>
        public void RemoveNotificationTrigger(NotificationTrigger trigger) =>
            requestEngine.ExecuteRequest(HtmlFunction.RemoveSubObject, new RemoveTriggerParameters(trigger));

        /// <summary>
        /// Asynchronously remove a notification trigger from an object.
        /// </summary>
        /// <param name="trigger">The notification trigger to remove.</param>
        public async Task RemoveNotificationTriggerAsync(NotificationTrigger trigger) =>
            await requestEngine.ExecuteRequestAsync(HtmlFunction.RemoveSubObject, new RemoveTriggerParameters(trigger)).ConfigureAwait(false);

        #endregion
        #region Clone Object

        /// <summary>
        /// Clone a sensor or group to another device or group.
        /// </summary>
        /// <param name="sourceObjectId">The ID of a sensor or group to clone.</param>
        /// <param name="cloneName">The name that should be given to the cloned object.</param>
        /// <param name="targetLocationObjectId">If this is a sensor, the ID of the device to clone to. If this is a group, the ID of the group to clone to.</param>
        /// <returns>The ID of the object that was created</returns>
        public int CloneObject(int sourceObjectId, string cloneName, int targetLocationObjectId) =>
            CloneObject(new CloneSensorOrGroupParameters(sourceObjectId, cloneName, targetLocationObjectId));

        /// <summary>
        /// Clone a device to another group or probe.
        /// </summary>
        /// <param name="deviceId">The ID of the device to clone.</param>
        /// <param name="cloneName">The name that should be given to the cloned device.</param>
        /// <param name="host">The hostname or IP Address that should be assigned to the new device.</param>
        /// <param name="targetLocationObjectId">The group or probe the device should be cloned to.</param>
        public int CloneObject(int deviceId, string cloneName, string host, int targetLocationObjectId) =>
            CloneObject(new CloneDeviceParameters(deviceId, cloneName, targetLocationObjectId, host));

        private int CloneObject(CloneSensorOrGroupParameters parameters) =>
            Amend(requestEngine.ExecuteRequest(CommandFunction.DuplicateObject, parameters, ResponseParser.CloneRequestParser), ResponseParser.CloneResponseParser);

        /// <summary>
        /// Asynchronously clone a sensor or group to another device or group.
        /// </summary>
        /// <param name="sourceObjectId">The ID of a sensor or group to clone.</param>
        /// <param name="cloneName">The name that should be given to the cloned object.</param>
        /// <param name="targetLocationObjectId">If this is a sensor, the ID of the device to clone to. If this is a group, the ID of the group to clone to.</param>
        /// <returns>The ID of the object that was created</returns>
        public async Task<int> CloneObjectAsync(int sourceObjectId, string cloneName, int targetLocationObjectId) =>
            await CloneObjectAsync(new CloneSensorOrGroupParameters(sourceObjectId, cloneName, targetLocationObjectId)).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously clone a device to another group or probe.
        /// </summary>
        /// <param name="deviceId">The ID of the device to clone.</param>
        /// <param name="cloneName">The name that should be given to the cloned device.</param>
        /// <param name="host">The hostname or IP Address that should be assigned to the new device.</param>
        /// <param name="targetLocationObjectId">The group or probe the device should be cloned to.</param>
        public async Task<int> CloneObjectAsync(int deviceId, string cloneName, string host, int targetLocationObjectId) =>
            await CloneObjectAsync(new CloneDeviceParameters(deviceId, cloneName, targetLocationObjectId, host)).ConfigureAwait(false);

        private async Task<int> CloneObjectAsync(CloneSensorOrGroupParameters parameters) =>
            Amend(
                await requestEngine.ExecuteRequestAsync(
                    CommandFunction.DuplicateObject,
                    parameters,
                    async r => await Task.FromResult(ResponseParser.CloneRequestParser(r)).ConfigureAwait(false)
                ).ConfigureAwait(false), ResponseParser.CloneResponseParser
            );

        #endregion
        #region Get Object Properties

        /// <summary>
        /// Retrieve properties and settings of a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">ID of the sensor to retrieve settings for.</param>
        /// <returns>All settings of the specified sensor.</returns>
        public SensorSettings GetSensorProperties(int sensorId) =>
            GetObjectProperties<SensorSettings>(sensorId, ObjectType.Sensor);

        /// <summary>
        /// Asynchronously retrieve properties and settings of a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">ID of the sensor to retrieve settings for.</param>
        /// <returns>All settings of the specified sensor.</returns>
        public async Task<SensorSettings> GetSensorPropertiesAsync(int sensorId) =>
            await GetObjectPropertiesAsync<SensorSettings>(sensorId, ObjectType.Sensor).ConfigureAwait(false);

        /// <summary>
        /// Retrieve properties and settings of a PRTG Device.
        /// </summary>
        /// <param name="deviceId">ID of the device to retrieve settings for.</param>
        /// <returns>All settings of the specified device.</returns>
        public DeviceSettings GetDeviceProperties(int deviceId) =>
            GetObjectProperties<DeviceSettings>(deviceId, ObjectType.Device);

        /// <summary>
        /// Asynchronously retrieve properties and settings of a PRTG Device.
        /// </summary>
        /// <param name="deviceId">ID of the device to retrieve settings for.</param>
        /// <returns>All settings of the specified device.</returns>
        public async Task<DeviceSettings> GetDevicePropertiesAsync(int deviceId) =>
            await GetObjectPropertiesAsync<DeviceSettings>(deviceId, ObjectType.Device).ConfigureAwait(false);

        /// <summary>
        /// Retrieve properties and settings of a PRTG Group.
        /// </summary>
        /// <param name="groupId">ID of the group to retrieve settings for.</param>
        /// <returns>All settings of the specified group.</returns>
        public GroupSettings GetGroupProperties(int groupId) =>
            GetObjectProperties<GroupSettings>(groupId, ObjectType.Group);

        /// <summary>
        /// Asynchronously retrieve properties and settings of a PRTG Group.
        /// </summary>
        /// <param name="groupId">ID of the group to retrieve settings for.</param>
        /// <returns>All settings of the specified group.</returns>
        public async Task<GroupSettings> GetGroupPropertiesAsync(int groupId) =>
            await GetObjectPropertiesAsync<GroupSettings>(groupId, ObjectType.Group).ConfigureAwait(false);

        /// <summary>
        /// Retrieve properties and settings of a PRTG Probe.
        /// </summary>
        /// <param name="probeId">ID of the probe to retrieve settings for.</param>
        /// <returns>All settings of the specified probe.</returns>
        public ProbeSettings GetProbeProperties(int probeId) =>
            GetObjectProperties<ProbeSettings>(probeId, ObjectType.Probe);

        /// <summary>
        /// Asynchronously retrieve properties and settings of a PRTG Probe.
        /// </summary>
        /// <param name="probeId">ID of the probe to retrieve settings for.</param>
        /// <returns>All settings of the specified probe.</returns>
        public async Task<ProbeSettings> GetProbePropertiesAsync(int probeId) =>
            await GetObjectPropertiesAsync<ProbeSettings>(probeId, ObjectType.Probe).ConfigureAwait(false);

        /// <summary>
        /// Retrieve all raw properties and settings of a PRTG Object. Note: objects may have additional properties
        /// that cannot be retrieved via this method.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve settings and properties for.</param>
        /// <param name="objectType">The type of object to retrieve settings and properties for.</param>
        /// <returns>A dictionary mapping all discoverable properties to raw values.</returns>
        public Dictionary<string, string> GetObjectPropertiesRaw(int objectId, ObjectType objectType) =>
            ObjectSettings.GetDictionary(GetObjectPropertiesRawInternal(objectId, objectType));

        /// <summary>
        /// Asynchronously retrieve all raw properties and settings of a PRTG Object. Note: objects may have additional properties
        /// that cannot be retrieved via this method.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve settings and properties for.</param>
        /// <param name="objectType">The type of object to retrieve settings and properties for.</param>
        /// <returns>A dictionary mapping all discoverable properties to raw values.</returns>
        public async Task<Dictionary<string, string>> GetObjectPropertiesRawAsync(int objectId, ObjectType objectType) =>
            ObjectSettings.GetDictionary(await GetObjectPropertiesRawInternalAsync(objectId, objectType).ConfigureAwait(false));

        /// <summary>
        /// Retrieve a type safe property from a PRTG Server.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve the property from.</param>
        /// <param name="property">The well known property to retrieve.</param>
        /// <returns>A type safe representation of the specified object.</returns>
        public object GetObjectProperty(int objectId, ObjectProperty property)
        {
            var rawName = BaseSetObjectPropertyParameters<ObjectProperty>.GetParameterName(property);

            var rawValue = GetObjectPropertyRaw(objectId, rawName);

            return XmlSerializer.DeserializeRawPropertyValue(property, rawName, rawValue);
        }

        /// <summary>
        /// Asynchronously retrieve a type safe property from a PRTG Server.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve the property from.</param>
        /// <param name="property">The well known property to retrieve.</param>
        /// <returns>A type safe representation of the specified property.</returns>
        public async Task<object> GetObjectPropertyAsync(int objectId, ObjectProperty property)
        {
            var rawName = BaseSetObjectPropertyParameters<ObjectProperty>.GetParameterName(property);

            var rawValue = await GetObjectPropertyRawAsync(objectId, rawName).ConfigureAwait(false);

            return XmlSerializer.DeserializeRawPropertyValue(property, rawName, rawValue);
        }

        /// <summary>
        /// Retrieve a type safe property from a PRTG Server, cast to its actual type. If the object is not of the type specified,
        /// an <see cref="InvalidCastException"/> will be thrown.
        /// </summary>
        /// <typeparam name="T">The type to cast the object to. If the object</typeparam>
        /// <param name="objectId">The ID of the object to retrieve the property from.</param>
        /// <param name="property">The well known property to retrieve.</param>
        /// <exception cref="InvalidCastException"/>
        /// <returns>A type safe representation of the specified property, cast to its actual type.</returns>
        public T GetObjectProperty<T>(int objectId, ObjectProperty property) =>
            ResponseParser.GetTypedProperty<T>(GetObjectProperty(objectId, property));

        /// <summary>
        /// Asynchronously retrieve a type safe property from a PRTG Server, cast to its actual type. If the object is not of the type specified,
        /// an <see cref="InvalidCastException"/> will be thrown.
        /// </summary>
        /// <typeparam name="T">The type to cast the object to. If the object</typeparam>
        /// <param name="objectId">The ID of the object to retrieve the property from.</param>
        /// <param name="property">The well known property to retrieve.</param>
        /// <exception cref="InvalidCastException"/>
        /// <returns>A type safe representation of the specified property, cast to its actual type.</returns>
        public async Task<T> GetObjectPropertyAsync<T>(int objectId, ObjectProperty property) =>
            ResponseParser.GetTypedProperty<T>(await GetObjectPropertyAsync(objectId, property).ConfigureAwait(false));

        /// <summary>
        /// Retrieve unsupported properties and settings of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose property should be retrieved.</param>
        /// <param name="property">The property of the object to retrieve. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <returns>The raw value of the object's property.</returns>
        public string GetObjectPropertyRaw(int objectId, string property)
        {
            var parameters = new GetObjectPropertyRawParameters(objectId, property);

            var response = requestEngine.ExecuteRequest(RequestParser.GetGetObjectPropertyFunction(property), parameters);

            return ResponseParser.ValidateRawObjectProperty(response, parameters);
        }

        /// <summary>
        /// Asynchronously retrieve unsupported properties and settings of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose property should be retrieved.</param>
        /// <param name="property">The property of the object to retrieve. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <returns>The raw value of the object's property.</returns>
        public async Task<string> GetObjectPropertyRawAsync(int objectId, string property)
        {
            var parameters = new GetObjectPropertyRawParameters(objectId, property);

            var response = await requestEngine.ExecuteRequestAsync(RequestParser.GetGetObjectPropertyFunction(property), parameters).ConfigureAwait(false);

            return ResponseParser.ValidateRawObjectProperty(response, parameters);
        }

        private T GetObjectProperties<T>(int objectId, ObjectType objectType)
        {
            var response = GetObjectPropertiesRawInternal(objectId, objectType);

            var data = ResponseParser.GetObjectProperties<T>(response);

            if (data is TableSettings)
            {
                var schedule = ((TableSettings) (object) data).Schedule;

                if (schedule != null)
                {
                    schedule.LazyXml = new Lazy<XDocument>(() => GetObjectsXml(new ScheduleParameters(schedule.Id)));
                }
            }

            return data;
        }

        private async Task<T> GetObjectPropertiesAsync<T>(int objectId, ObjectType objectType)
        {
            var response = await GetObjectPropertiesRawInternalAsync(objectId, objectType).ConfigureAwait(false);

            var data = ResponseParser.GetObjectProperties<T>(response);

            if (data is TableSettings)
            {
                var schedule = ((TableSettings)(object)data).Schedule;

                if (schedule != null)
                {
                    var xDoc = await GetObjectsXmlAsync(new ScheduleParameters(schedule.Id)).ConfigureAwait(false);
                    schedule.LazyXml = new Lazy<XDocument>(() => xDoc);
                }
            }

            return data;
        }

        private string GetObjectPropertiesRawInternal(int objectId, ObjectType objectType) =>
            requestEngine.ExecuteRequest(HtmlFunction.ObjectData, new GetObjectPropertyParameters(objectId, objectType));

        private async Task<string> GetObjectPropertiesRawInternalAsync(int objectId, ObjectType objectType) =>
            await requestEngine.ExecuteRequestAsync(HtmlFunction.ObjectData, new GetObjectPropertyParameters(objectId, objectType)).ConfigureAwait(false);

        #endregion
        #region Set Object Properties
            #region Normal

        /// <summary>
        /// Modify properties and settings of a PRTG Object.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="property">The property of the object to modify.</param>
        /// <param name="value">The value to set the object's property to.</param>
        public void SetObjectProperty(int objectId, ObjectProperty property, object value) =>
            SetObjectProperty(new[] {objectId}, property, value);  

        /// <summary>
        /// Modify properties and settings of one or more PRTG Objects.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="property">The property of each object to modify.</param>
        /// <param name="value">The value to set each object's property to.</param>
        public void SetObjectProperty(int[] objectIds, ObjectProperty property, object value) =>
            SetObjectProperty(objectIds, new PropertyParameter(property, value));

        /// <summary>
        /// Asynchronously modify properties and settings of a PRTG Object.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="property">The property of the object to modify.</param>
        /// <param name="value">The value to set the object's property to.</param>
        public async Task SetObjectPropertyAsync(int objectId, ObjectProperty property, object value) =>
            await SetObjectPropertyAsync(new[] {objectId}, property, value).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously odify properties and settings of one or more PRTG Objects.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="property">The property of each object to modify.</param>
        /// <param name="value">The value to set each object's property to.</param>
        public async Task SetObjectPropertyAsync(int[] objectIds, ObjectProperty property, object value) =>
            await SetObjectPropertyAsync(objectIds, new PropertyParameter(property, value)).ConfigureAwait(false);

            #endregion Normal
            #region Normal (Multiple

        /// <summary>
        /// Modify multiple properties of a PRTG Object.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectProperty(int objectId, params PropertyParameter[] parameters) =>
            SetObjectProperty(new[] {objectId}, parameters);

        /// <summary>
        /// Modify multiple properties of one or more PRTG Objects.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectProperty(int[] objectIds, params PropertyParameter[] parameters) =>
            SetObjectProperty(CreateSetObjectPropertyParameters(objectIds, parameters), objectIds.Length);

        /// <summary>
        /// Asynchronously modify multiple properties of a PRTG Object.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyAsync(int objectId, params PropertyParameter[] parameters) =>
            await SetObjectPropertyAsync(new[] { objectId }, parameters).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously modify multiple properties of one or more PRTG Objects.<para/>
        /// Each <see cref="ObjectProperty"/> corresponds with a Property of a type derived from <see cref="ObjectSettings"/>.<para/>
        /// If PrtgAPI cannot convert the specified value to the type required by the property, PrtgAPI will throw an exception indicating the type that was expected.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyAsync(int[] objectIds, params PropertyParameter[] parameters) =>
            await SetObjectPropertyAsync(await CreateSetObjectPropertyParametersAsync(objectIds, parameters).ConfigureAwait(false), objectIds.Length).ConfigureAwait(false);

            #endregion
            #region Channel

        /// <summary>
        /// Modify channel properties for a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel to modify.</param>
        /// <param name="property">The property of the channel to modify</param>
        /// <param name="value">The value to set the channel's property to.</param>
        public void SetObjectProperty(int sensorId, int channelId, ChannelProperty property, object value) =>
            SetObjectProperty(new[] {sensorId}, channelId, property, value);

        /// <summary>
        /// Modify channel properties for one or more PRTG Sensors.
        /// </summary>
        /// <param name="sensorIds">The IDs of the sensors whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel of each sensor to modify.</param>
        /// <param name="property">The property of each channel to modify</param>
        /// <param name="value">The value to set each channel's property to.</param>
        public void SetObjectProperty(int[] sensorIds, int channelId, ChannelProperty property, object value) =>
            SetObjectProperty(sensorIds, channelId, new ChannelParameter(property, value));
        
        /// <summary>
        /// Asynchronously modify channel properties for a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel to modify.</param>
        /// <param name="property">The property of the channel to modify</param>
        /// <param name="value">The value to set the channel's property to.</param>
        public async Task SetObjectPropertyAsync(int sensorId, int channelId, ChannelProperty property, object value) =>
            await SetObjectPropertyAsync(new[] {sensorId}, channelId, property, value).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously modify channel properties for one or more PRTG Sensors.
        /// </summary>
        /// <param name="sensorIds">The IDs of the sensors whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel of each sensor to modify.</param>
        /// <param name="property">The property of each channel to modify</param>
        /// <param name="value">The value to set each channel's property to.</param>
        public async Task SetObjectPropertyAsync(int[] sensorIds, int channelId, ChannelProperty property, object value) =>
            await SetObjectPropertyAsync(sensorIds, channelId, new ChannelParameter(property, value)).ConfigureAwait(false);

            #endregion Channel
            #region Channel (Multiple)

        /// <summary>
        /// Modify multiple channel properties for a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel to modify.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectProperty(int sensorId, int channelId, params ChannelParameter[] parameters) =>
            SetObjectProperty(new[] { sensorId }, channelId, parameters);

        /// <summary>
        /// Modify multiple channel properties for one or more PRTG Sensors.
        /// </summary>
        /// <param name="sensorIds">The IDs of the sensors whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel of each sensor to modify.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectProperty(int[] sensorIds, int channelId, params ChannelParameter[] parameters) =>
            GetVersionClient<ChannelParameter, ChannelProperty>(parameters.ToList()).SetChannelProperty(sensorIds, channelId, null, parameters);

        /// <summary>
        /// Asynchronously modify multiple channel properties for a PRTG Sensor.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel to modify.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyAsync(int sensorId, int channelId, params ChannelParameter[] parameters) =>
            await SetObjectPropertyAsync(new[] { sensorId }, channelId, parameters).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously modify multiple channel properties for one or more PRTG Sensors.
        /// </summary>
        /// <param name="sensorIds">The IDs of the sensors whose channels should be modified.</param>
        /// <param name="channelId">The ID of the channel of each sensor to modify.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyAsync(int[] sensorIds, int channelId, params ChannelParameter[] parameters) =>
            await GetVersionClient<ChannelParameter, ChannelProperty>(parameters.ToList()).SetChannelPropertyAsync(sensorIds, channelId, null, parameters).ConfigureAwait(false);

            #endregion
            #region Custom

        /// <summary>
        /// Modify unsupported properties and settings of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="property">The property of the object to modify. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <param name="value">The value to set the object's property to. For radio buttons and dropdown lists, this is the integer found in the 'value' attribute.</param>
        public void SetObjectPropertyRaw(int objectId, string property, string value) =>
            SetObjectPropertyRaw(new[] {objectId}, property, value);

        /// <summary>
        /// Modify unsupported properties and settings of one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="property">The property of each object to modify. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <param name="value">The value to set each object's property to. For radio buttons and dropdown lists, this is the integer found in the 'value' attribute.</param>
        public void SetObjectPropertyRaw(int[] objectIds, string property, string value) =>
            SetObjectPropertyRaw(objectIds, new CustomParameter(property, value));

        /// <summary>
        /// Asynchronously modify unsupported properties and settings of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="property">The property of the object to modify. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <param name="value">The value to set the object's property to. For radio buttons and dropdown lists, this is the integer found in the 'value' attribute.</param>
        public async Task SetObjectPropertyRawAsync(int objectId, string property, string value) =>
            await SetObjectPropertyRawAsync(new[] {objectId}, property, value).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously modify unsupported properties and settings of one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="property">The property of each object to modify. This can be typically discovered by inspecting the "name" attribute of the properties' &lt;input/&gt; tag on the Settings page of PRTG.<para/>
        /// If the properties name ends in an underscore, this must be included.</param>
        /// <param name="value">The value to set each object's property to. For radio buttons and dropdown lists, this is the integer found in the 'value' attribute.</param>
        public async Task SetObjectPropertyRawAsync(int[] objectIds, string property, string value) =>
            await SetObjectPropertyRawAsync(objectIds, new CustomParameter(property, value)).ConfigureAwait(false);

            #endregion
            #region Custom (Multiple)

        /// <summary>
        /// Modify multiple unsupported properties of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectPropertyRaw(int objectId, params CustomParameter[] parameters) =>
            SetObjectPropertyRaw(new[] { objectId }, parameters);

        /// <summary>
        /// Modify multiple unsupported properties of one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public void SetObjectPropertyRaw(int[] objectIds, params CustomParameter[] parameters) =>
            SetObjectProperty(new SetObjectPropertyParameters(objectIds, parameters), objectIds.Length);

        /// <summary>
        /// Asynchronously modify multiple unsupported properties of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyRawAsync(int objectId, params CustomParameter[] parameters) =>
            await SetObjectPropertyRawAsync(new[] { objectId }, parameters).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously modify multiple unsupported properties of one or more PRTG Objects.
        /// </summary>
        /// <param name="objectIds">The IDs of the objects whose properties should be modified.</param>
        /// <param name="parameters">A set of parameters describing the properties and their values to process.</param>
        public async Task SetObjectPropertyRawAsync(int[] objectIds, params CustomParameter[] parameters) =>
            await SetObjectPropertyAsync(new SetObjectPropertyParameters(objectIds, parameters), objectIds.Length).ConfigureAwait(false);

            #endregion

        internal void SetObjectProperty<T>(BaseSetObjectPropertyParameters<T> parameters, int numObjectIds) =>
            requestEngine.ExecuteRequest(HtmlFunction.EditSettings, parameters, m => ResponseParser.ParseSetObjectPropertyUrl(numObjectIds, m));

        internal async Task SetObjectPropertyAsync<T>(BaseSetObjectPropertyParameters<T> parameters, int numObjectIds) =>
            await requestEngine.ExecuteRequestAsync(HtmlFunction.EditSettings, parameters, m => Task.FromResult(ResponseParser.ParseSetObjectPropertyUrl(numObjectIds, m))).ConfigureAwait(false);

        private SetObjectPropertyParameters CreateSetObjectPropertyParameters(int[] objectIds, params PropertyParameter[] @params)
        {
            foreach (var prop in @params)
            {
                var attrib = prop.Property.GetEnumAttribute<TypeAttribute>();

                if (attrib != null)
                {
                    try
                    {
                        var method = attrib.Class.GetMethod("Resolve", BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Static);

                        if (method != null)
                        {
                            prop.Value = method.Invoke(null, new[] { this, prop.Value });
                        }
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw ex.InnerException;
                    }
                }
            }
            
            var parameters = new SetObjectPropertyParameters(objectIds, @params);

            return parameters;
        }

        private async Task<SetObjectPropertyParameters> CreateSetObjectPropertyParametersAsync(int[] objectIds, params PropertyParameter[] @params)
        {
            foreach (var prop in @params)
            {
                var attrib = prop.Property.GetEnumAttribute<TypeAttribute>();

                if (attrib != null)
                {
                    try
                    {
                        var method = attrib.Class.GetMethod("ResolveAsync", BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Static);

                        if (method != null)
                        {
                            var task = ((Task)method.Invoke(null, new[] { this, prop.Value }));

                            await task.ConfigureAwait(false);

                            prop.Value = task.GetType().GetProperty("Result").GetValue(task);
                        }
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw ex.InnerException;
                    }
                }
            }

            var parameters = new SetObjectPropertyParameters(objectIds, @params);

            return parameters;
        }

        #endregion
        #region System Administration

        /// <summary>
        /// Request PRTG generate a backup of the PRTG Configuration Database.<para/>
        /// When executed, this method will request PRTG store a backup of its configuration database under
        /// the Configuration Auto-Backups folder after first writing the current running configuration to disk.<para/>
        /// Depending on the size of your database, this may take several seconds to complete. Note that PRTG always creates
        /// its backup asynchronously; as such when this method returns the backup may not have fully completed.<para/>
        /// By default, configuration backups are stored under C:\ProgramData\Paessler\PRTG Network Monitor\Configuration Auto-Backups.
        /// </summary>
        public void BackupConfigDatabase() =>
            requestEngine.ExecuteRequest(CommandFunction.SaveNow, new Parameters.Parameters());

        /// <summary>
        /// Asynchronously request PRTG generate a backup of the PRTG Configuration Database.<para/>
        /// When executed, this method will request PRTG store a backup of its configuration database under
        /// the Configuration Auto-Backups folder after first writing the current running configuration to disk.<para/>
        /// Depending on the size of your database, this may take several seconds to complete. Note that PRTG always creates
        /// its backup asynchronously; as such when this method returns the backup may not have fully completed.<para/>
        /// By default, configuration backups are stored under C:\ProgramData\Paessler\PRTG Network Monitor\Configuration Auto-Backups.
        /// </summary>
        public async Task BackupConfigDatabaseAsync() =>
            await requestEngine.ExecuteRequestAsync(CommandFunction.SaveNow, new Parameters.Parameters()).ConfigureAwait(false);

        /// <summary>
        /// Clear cached data used by PRTG, including map, graph and authentication caches. Note: clearing certain cache types may result in a restart of the PRTG Core Server.
        /// See each cache type for further details.
        /// </summary>
        /// <param name="cache">The type of cache to clear. Note: clearing certain cache types may result in a restart of the PRTG Core Server.
        /// See each cache type for further details.</param>
        public void ClearSystemCache(SystemCacheType cache) =>
            requestEngine.ExecuteRequest(RequestParser.GetClearSystemCacheFunction(cache), new Parameters.Parameters());

        /// <summary>
        /// Asynchronously clear cached data used by PRTG, including map, graph and authentication caches. Note: clearing certain cache types may result in a restart of the PRTG Core Server.
        /// See each cache type for further details.
        /// </summary>
        /// <param name="cache">The type of cache to clear. Note: clearing certain cache types may result in a restart of the PRTG Core Server.
        /// See each cache type for further details.</param>
        public async Task ClearSystemCacheAsync(SystemCacheType cache) =>
            await requestEngine.ExecuteRequestAsync(RequestParser.GetClearSystemCacheFunction(cache), new Parameters.Parameters()).ConfigureAwait(false);
        
        /// <summary>
        /// Reload config files including sensor lookups, device icons and report templates used by PRTG.
        /// </summary>
        /// <param name="fileType">The type of files to reload.</param>
        public void LoadConfigFiles(ConfigFileType fileType) =>
            requestEngine.ExecuteRequest(RequestParser.GetLoadSystemFilesFunction(fileType), new Parameters.Parameters());

        /// <summary>
        /// Asymchronously reload config files including sensor lookups, device icons and report templates used by PRTG.
        /// </summary>
        /// <param name="fileType">The type of files to reload.</param>
        public async Task LoadConfigFilesAsync(ConfigFileType fileType) =>
            await requestEngine.ExecuteRequestAsync(RequestParser.GetLoadSystemFilesFunction(fileType), new Parameters.Parameters()).ConfigureAwait(false);

        /// <summary>
        /// Restarts the PRTG Probe Service of a specified PRTG Probe. If no probe ID is specified, the PRTG Probe Service will be restarted on all PRTG Probes.<para/>
        /// By default, PrtgAPI will wait 5 seconds between each probing attempt to confirm whether all probes have successfully restarted.<para/>
        /// If a progress callback is specified, it is up to the programmer to specify the wait duration between each request. If at any time
        /// the progress callback returns false, PrtgAPI will stop waiting for all probes to restart.
        /// </summary>
        /// <param name="probeId">The ID of the probe to restart. If this value is null, the PRTG Probe Service of all probes will be restarted.</param>
        /// <param name="waitForRestart">Whether to wait for the Probe Service on all probes to restart before completing this method.</param>
        /// <param name="progressCallback">A callback method to execute upon each request against PRTG to check whether all probes have restarted.</param>
        public void RestartProbe(int? probeId, bool waitForRestart = false, Func<List<RestartProbeProgress>, bool> progressCallback = null)
        {
            var restartTime = DateTime.Now;

            requestEngine.ExecuteRequest(CommandFunction.RestartProbes, new RestartProbeParameters(probeId));

            if (waitForRestart)
            {
                var probe = probeId == null ? GetProbes() : GetProbes(Property.Id, probeId);
                WaitForProbeRestart(restartTime, probe, progressCallback);
            }
        }

        /// <summary>
        /// Asynchronously restarts the PRTG Probe Service of a specified PRTG Probe. If no probe ID is specified, the PRTG Probe Service will be restarted on all PRTG Probes.<para/>
        /// By default, PrtgAPI will wait 5 seconds between each probing attempt to confirm whether all probes have successfully restarted.<para/>
        /// If a progress callback is specified, it is up to the programmer to specify the wait duration between each request. If at any time
        /// the progress callback returns false, PrtgAPI will stop waiting for all probes to restart.
        /// </summary>
        /// <param name="probeId">The ID of the probe to restart. If this value is null, the PRTG Probe Service of all probes will be restarted.</param>
        /// <param name="waitForRestart">Whether to wait for the Probe Service on all probes to restart before completing this method.</param>
        /// <param name="progressCallback">A callback method to execute upon each request against PRTG to check whether all probes have restarted.</param>
        public async Task RestartProbeAsync(int? probeId, bool waitForRestart = false, Func<List<RestartProbeProgress>, bool> progressCallback = null)
        {
            var restartTime = DateTime.Now;

            await requestEngine.ExecuteRequestAsync(CommandFunction.RestartProbes, new RestartProbeParameters(probeId)).ConfigureAwait(false);

            if (waitForRestart)
            {
                var probe = probeId == null ? await GetProbesAsync().ConfigureAwait(false) : await GetProbesAsync(Property.Id, probeId).ConfigureAwait(false);
                await WaitForProbeRestartAsync(restartTime, probe, progressCallback).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Restarts the PRTG Core Service. This will cause PRTG to disconnect all users and become completely unavailable while the service restarts.<para/>
        /// If PRTG is part of a cluster, only the server specified by the current <see cref="PrtgClient"/> will be restarted.<para/>
        /// By default, PrtgAPI will wait 5 seconds between each probing attempt to confirm whether PRTG has successfully restarted.<para/>
        /// If a progress callback is specified, it is up to the programmer to specify the wait duration between each request. If at any time
        /// the progress callback returns false, PrtgAPI will stop waiting for the core to restart.
        /// </summary>
        /// <param name="waitForRestart">Whether wait for the Core Service to restart before completing this method.</param>
        /// <param name="progressCallback">A callback method to execute upon each request against PRTG to check whether PRTG has restarted.</param>
        public void RestartCore(bool waitForRestart = false, Func<RestartCoreStage, bool> progressCallback = null)
        {
            DateTime restartTime = DateTime.Now;

            requestEngine.ExecuteRequest(CommandFunction.RestartServer, new Parameters.Parameters());

            if (waitForRestart)
                WaitForCoreRestart(restartTime, progressCallback);
        }

        /// <summary>
        /// Asynchronously restarts the PRTG Core Service. This will cause PRTG to disconnect all users and become completely unavailable while the service restarts.<para/>
        /// If PRTG is part of a cluster, only the server specified by the current <see cref="PrtgClient"/> will be restarted.<para/>
        /// By default, PrtgAPI will wait 5 seconds between each probing attempt to confirm whether PRTG has successfully restarted.<para/>
        /// If a progress callback is specified, it is up to the programmer to specify the wait duration between each request. If at any time
        /// the progress callback returns false, PrtgAPI will stop waiting for the core to restart.
        /// </summary>
        public async Task RestartCoreAsync(bool waitForRestart = false, Func<RestartCoreStage, bool> progressCallback = null)
        {
            DateTime restartTime = DateTime.Now;

            await requestEngine.ExecuteRequestAsync(CommandFunction.RestartServer, new Parameters.Parameters()).ConfigureAwait(false);

            if (waitForRestart)
                await WaitForCoreRestartAsync(restartTime, progressCallback).ConfigureAwait(false);
        }

        #endregion
        #region Miscellaneous

        /// <summary>
        /// Request an object or any children of an one or more objects refresh themselves immediately.
        /// </summary>
        /// <param name="objectIds">The IDs of the Sensors and/or the IDs of the Probes, Groups or Devices whose child sensors should be refreshed.</param>
        public void RefreshObject(params int[] objectIds) => requestEngine.ExecuteRequest(CommandFunction.ScanNow, new BaseMultiActionParameters(objectIds));

        /// <summary>
        /// Asynchronously request an object or any children of one or more objects refresh themselves immediately.
        /// </summary>
        /// <param name="objectIds">The IDs of the Sensors and/or the IDs of the Probes, Groups or Devices whose child sensors should be refreshed.</param>
        public async Task RefreshObjectAsync(params int[] objectIds) => await requestEngine.ExecuteRequestAsync(CommandFunction.ScanNow, new BaseMultiActionParameters(objectIds)).ConfigureAwait(false);

        /// <summary>
        /// Automatically create sensors under an object based on the object's (or it's children's) device type.
        /// </summary>
        /// <param name="objectId">The object to run Auto-Discovery for (such as a device or group).</param>
        /// <param name="templates">An optional list of device templates to use for performing the auto-discovery.</param>
        public void AutoDiscover(int objectId, params DeviceTemplate[] templates) =>
            requestEngine.ExecuteRequest(CommandFunction.DiscoverNow, new AutoDiscoverParameters(objectId, templates));

        /// <summary>
        /// Asynchronously automatically create sensors under an object based on the object's (or it's children's) device type.
        /// </summary>
        /// <param name="objectId">The object to run Auto-Discovery for (such as a device or group).</param>
        /// <param name="templates">An optional list of device templates to use for performing the auto-discovery.</param>
        public async Task AutoDiscoverAsync(int objectId, params DeviceTemplate[] templates) =>
            await requestEngine.ExecuteRequestAsync(CommandFunction.DiscoverNow, new AutoDiscoverParameters(objectId, templates)).ConfigureAwait(false);

        /// <summary>
        /// Move the position of an object up or down under its parent within the PRTG User Interface.
        /// </summary>
        /// <param name="objectId">The object to reposition.</param>
        /// <param name="position">The direction to move in.</param>
        public void SetPosition(int objectId, Position position) => requestEngine.ExecuteRequest(CommandFunction.SetPosition, new SetPositionParameters(objectId, position));

        /// <summary>
        /// Set the absolute position of an object under its parent within the PRTG User Interface
        /// </summary>
        /// <param name="obj">The object to reposition.</param>
        /// <param name="position">The position to move the object to. If this value is higher than the total number of objects under the parent node, the object will be moved to the last possible position.</param>
        public void SetPosition(SensorOrDeviceOrGroupOrProbe obj, int position) => requestEngine.ExecuteRequest(CommandFunction.SetPosition, new SetPositionParameters(obj, position));

        /// <summary>
        /// Asynchronously move the position of an object up or down under its parent within the PRTG User Interface.
        /// </summary>
        /// <param name="objectId">The object to reposition.</param>
        /// <param name="position">The direction to move in.</param>
        public async Task SetPositionAsync(int objectId, Position position) => await requestEngine.ExecuteRequestAsync(CommandFunction.SetPosition, new SetPositionParameters(objectId, position)).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously set the absolute position of an object under its parent within the PRTG User Interface
        /// </summary>
        /// <param name="obj">The object to reposition.</param>
        /// <param name="position">The position to move the object to. If this value is higher than the total number of objects under the parent node, the object will be moved to the last possible position.</param>
        public async Task SetPositionAsync(SensorOrDeviceOrGroupOrProbe obj, int position) => await requestEngine.ExecuteRequestAsync(CommandFunction.SetPosition, new SetPositionParameters(obj, position)).ConfigureAwait(false);

        /// <summary>
        /// Move a device or group (excluding the root group) to another group or probe within PRTG.
        /// </summary>
        /// <param name="objectId">The ID of a device or group to move.</param>
        /// <param name="destinationId">The group or probe to move the object to.</param>
        public void MoveObject(int objectId, int destinationId) => requestEngine.ExecuteRequest(CommandFunction.MoveObjectNow, new MoveObjectParameters(objectId, destinationId));

        /// <summary>
        /// Asynchronously Move a device or group (excluding the root group) to another group or probe within PRTG.
        /// </summary>
        /// <param name="objectId">The ID of a device or group to move.</param>
        /// <param name="destinationId">The group or probe to move the object to.</param>
        public async Task MoveObjectAsync(int objectId, int destinationId) => await requestEngine.ExecuteRequestAsync(CommandFunction.MoveObjectNow, new MoveObjectParameters(objectId, destinationId)).ConfigureAwait(false);

        /// <summary>
        /// Sort the children of a device, group or probe alphabetically.
        /// </summary>
        /// <param name="objectId">The object to sort.</param>
        public void SortAlphabetically(int objectId) => requestEngine.ExecuteRequest(CommandFunction.SortSubObjects, new BaseActionParameters(objectId));

        /// <summary>
        /// Asynchronously sort the children of a device, group or probe alphabetically.
        /// </summary>
        /// <param name="objectId">The object to sort.</param>
        public async Task SortAlphabeticallyAsync(int objectId) => await requestEngine.ExecuteRequestAsync(CommandFunction.SortSubObjects, new BaseActionParameters(objectId)).ConfigureAwait(false);

        /// <summary>
        /// Permanently remove one or more objects such as a Sensor, Device, Group or Probe from PRTG. This cannot be undone.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to remove.</param>
        public void RemoveObject(params int[] objectIds) => requestEngine.ExecuteRequest(CommandFunction.DeleteObject, new DeleteParameters(objectIds));

        /// <summary>
        /// Asynchronously permanently remove one or more objects such as a Sensor, Device, Group or Probe from PRTG. This cannot be undone.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to remove.</param>
        public async Task RemoveObjectAsync(params int[] objectIds) => await requestEngine.ExecuteRequestAsync(CommandFunction.DeleteObject, new DeleteParameters(objectIds)).ConfigureAwait(false);

        /// <summary>
        /// Rename a Sensor, Device, Group or Probe within PRTG.
        /// </summary>
        /// <param name="objectId">ID of the object to rename.</param>
        /// <param name="name">New name to give the object.</param>
        public void RenameObject(int objectId, string name) => RenameObject(new[] {objectId}, name);

        /// <summary>
        /// Rename one or more Sensors, Devices, Groups or Probe within PRTG.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to rename.</param>
        /// <param name="name">New name to give the objects.</param>
        public void RenameObject(int[] objectIds, string name) => requestEngine.ExecuteRequest(CommandFunction.Rename, new RenameParameters(objectIds, name));

        /// <summary>
        /// Asynchronously rename a Sensor, Device, Group or Probe within PRTG.
        /// </summary>
        /// <param name="objectId">ID of the object to rename.</param>
        /// <param name="name">New name to give the object.</param>
        public async Task RenameObjectAsync(int objectId, string name) => await RenameObjectAsync(new[] {objectId}, name).ConfigureAwait(false);

        /// <summary>
        /// Asynchronously rename one or more Sensors, Devices, Groups or Probes within PRTG.
        /// </summary>
        /// <param name="objectIds">IDs of the objects to rename.</param>
        /// <param name="name">New name to give the objects.</param>
        public async Task RenameObjectAsync(int[] objectIds, string name) => await requestEngine.ExecuteRequestAsync(CommandFunction.Rename, new RenameParameters(objectIds, name)).ConfigureAwait(false);

        #endregion
    #endregion
#endregion

#region Unsorted

        /// <summary>
        /// Calcualte the total number of objects of a given type present on a PRTG Server.
        /// </summary>
        /// <param name="content">The type of object to total.</param>
        /// <returns>The total number of objects of the given type.</returns>
        public int GetTotalObjects(Content content) => Convert.ToInt32(GetObjectsRaw<PrtgObject>(new TotalObjectsParameters(content)).TotalCount);

        /// <summary>
        /// Asynchronously calcualte the total number of objects of a given type present on a PRTG Server.
        /// </summary>
        /// <param name="content">The type of object to total.</param>
        /// <returns>The total number of objects of the given type.</returns>
        public async Task<int> GetTotalObjectsAsync(Content content) => Convert.ToInt32((await GetObjectsRawAsync<PrtgObject>(new TotalObjectsParameters(content)).ConfigureAwait(false)).TotalCount);

        /// <summary>
        /// Retrieve the setting/state modification history of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve historical records for.</param>
        /// <returns>A list of all setting/state modifications to the specified object.</returns>
        public List<ModificationEvent> GetModificationHistory(int objectId) => Amend(GetObjects<ModificationEvent>(new ModificationHistoryParameters(objectId)), e => e.ObjectId = objectId);

        /// <summary>
        /// Asynchronously retrieve the setting/state modification history of a PRTG Object.
        /// </summary>
        /// <param name="objectId">The ID of the object to retrieve historical records for.</param>
        /// <returns>A list of all setting/state modifications to the specified object.</returns>
        public async Task<List<ModificationEvent>> GetModificationHistoryAsync(int objectId) => Amend(await GetObjectsAsync<ModificationEvent>(new ModificationHistoryParameters(objectId)).ConfigureAwait(false), e => e.ObjectId = objectId);

        /// <summary>
        /// Retrieve the historical values of a sensor's channels from within a specified time period.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve historical data for.</param>
        /// <param name="average">The time span (in seconds) to average results up to. For example, a value of 300 shows the average of results every 5 minutes. If a value of 0
        /// is used, PRTG will use the true interval of the sensor.<para/>
        /// Note: due to limitations of the PRTG API, value lookup labels can only be retrieved when the average is 0, while downtime information
        /// can only be retrieved when the average is not 0.</param>
        /// <param name="startDate">The start date and time to retrieve data from. If this value is null, records will be retrieved from the current date and time.</param>
        /// <param name="endDate">The end date and time to retrieve data to. If this value is null, records will be retrieved from one hour prior to <paramref name="startDate"/>.</param>
        /// <param name="count">Limit results to the specified number of items within the specified time period.</param>
        /// <returns>Historical data for the specified sensor within the desired date range.</returns>
        public List<SensorHistoryData> GetSensorHistory(int sensorId, int average = 300, DateTime? startDate = null, DateTime? endDate = null, int? count = null)
        {
            var parameters = new SensorHistoryParameters(sensorId, average, startDate, endDate, count);

            return GetSensorHistoryInternal(parameters);
        }

        internal List<SensorHistoryData> GetSensorHistoryInternal(SensorHistoryParameters parameters)
        {
            var items = GetObjects<SensorHistoryData>(parameters, XmlFunction.HistoricData, ResponseParser.ValidateSensorHistoryResponse);

            return ResponseParser.ParseSensorHistoryResponse(items, parameters.SensorId);
        }

        /// <summary>
        /// Asynchronously retrieve the historical values of a sensor's channels from within a specified time period.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve historical data for.</param>
        /// <param name="average">The time span (in seconds) to average results up to. For example, a value of 300 shows the average of results every 5 minutes. If a value of 0
        /// is used, PRTG will use the true interval of the sensor.<para/>
        /// Note: due to limitations of the PRTG API, value lookup labels can only be retrieved when the average is 0, while downtime information
        /// can only be retrieved when the average is not 0.</param>
        /// <param name="startDate">The start date and time to retrieve data from. If this value is null, records will be retrieved from the current date and time.</param>
        /// <param name="endDate">The end date and time to retrieve data to. If this value is null, records will be retrieved from one hour prior to <paramref name="startDate"/>.</param>
        /// <param name="count">Limit results to the specified number of items within the specified time period.</param>
        /// <returns>Historical data for the specified sensor within the desired date range.</returns>
        public async Task<List<SensorHistoryData>> GetSensorHistoryAsync(int sensorId, int average = 300, DateTime? startDate = null, DateTime? endDate = null, int? count = null)
        {
            var parameters = new SensorHistoryParameters(sensorId, average, startDate, endDate, count);

            return await GetSensorHistoryAsyncInternal(parameters).ConfigureAwait(false);
        }

        internal async Task<List<SensorHistoryData>> GetSensorHistoryAsyncInternal(SensorHistoryParameters parameters)
        {
            var items = await GetObjectsAsync<SensorHistoryData>(parameters, XmlFunction.HistoricData, ResponseParser.ValidateSensorHistoryResponse).ConfigureAwait(false);

            return ResponseParser.ParseSensorHistoryResponse(items, parameters.SensorId);
        }

        /// <summary>
        /// Stream historical values of a sensors channels from within a specified time period. When this method's response is enumerated,
        /// requests will be sent to PRTG as required in order to retrieve additional items.
        /// </summary>
        /// <param name="sensorId">The ID of the sensor to retrieve historical data for.</param>
        /// <param name="average">The time span (in seconds) to average results up to. For example, a value of 300 shows the average of results every 5 minutes. If a value of 0
        /// is used, PRTG will use the true interval of the sensor.<para/>
        /// Note: due to limitations of the PRTG API, value lookup labels can only be retrieved when the average is 0, while downtime information
        /// can only be retrieved when the average is not 0.</param>
        /// <param name="startDate">The start date and time to retrieve data from. If this value is null, records will be retrieved from the current date and time.</param>
        /// <param name="endDate">The end date and time to retrieve data to. If this value is null, records will be retrieved from one hour prior to <paramref name="startDate"/>.</param>
        /// <returns>A generator encapsulating a series of requests capable of streaming a response from a PRTG Server.</returns>
        public IEnumerable<SensorHistoryData> StreamSensorHistory(int sensorId, int average = 300, DateTime? startDate = null, DateTime? endDate = null)
        {
            var parameters = new SensorHistoryParameters(sensorId, average, startDate, endDate, null);

            return StreamSensorHistoryInternal(parameters, true);
        }

        private IEnumerable<SensorHistoryData> StreamSensorHistoryInternal(SensorHistoryParameters parameters, bool serial)
        {
            return StreamObjects(
                parameters,
                serial,
                () => GetSensorHistoryTotals(parameters),
                GetSensorHistoryAsyncInternal,
                GetSensorHistoryInternal
            );
        }

        internal int GetSensorHistoryTotals(SensorHistoryParameters parameters)
        {
            parameters.Count = 0;

            var data = GetObjectsRaw<SensorHistoryData>(parameters, XmlFunction.HistoricData, ResponseParser.ValidateSensorHistoryResponse);

            parameters.GetParameters().Remove(Parameter.Count);

            return Convert.ToInt32(data.TotalCount);
        }

        //todo: check all arguments we can in this file and make sure we validate input. when theres a chain of methods, validate on the inner most one except if we pass a parameter object, in which case validate both

        /// <summary>
        /// Retrieve configuration, status and version details from a PRTG Server.
        /// </summary>
        /// <returns>Status details of a PRTG Server.</returns>
        public ServerStatus GetStatus() => GetObject<ServerStatus>(JsonFunction.GetStatus, new BaseActionParameters(0));

        /// <summary>
        /// Asynchronously etrieve configuration, status and version details from a PRTG Server.
        /// </summary>
        /// <returns>Status details of a PRTG Server.</returns>
        public async Task<ServerStatus> GetStatusAsync() => await GetObjectAsync<ServerStatus>(JsonFunction.GetStatus, new BaseActionParameters(0)).ConfigureAwait(false);

        /// <summary>
        /// Resolve an address to its latitudinal and longitudinal coordinates. May spuriously return no results.
        /// </summary>
        /// <param name="address">The address to resolve.</param>
        /// <returns></returns>
        internal List<Location> ResolveAddress(string address) =>
            GetObject<GeoResult>(JsonFunction.GeoLocator, new ResolveAddressParameters(address), ResolveParser).Results.ToList();

        /// <summary>
        /// Asynchronously resolve an address to its latitudinal and longitudinal coordinates. May spuriously return no results.
        /// </summary>
        /// <param name="address">The address to resolve.</param>
        /// <returns></returns>
        internal async Task<List<Location>> ResolveAddressAsync(string address) =>
            (await GetObjectAsync<GeoResult>(JsonFunction.GeoLocator, new ResolveAddressParameters(address), m => Task.FromResult(ResolveParser(m))).ConfigureAwait(false)).Results.ToList();

        string ResolveParser(HttpResponseMessage message)
        {
            if (message.Content.Headers.ContentType.MediaType == "image/png" || message.StatusCode.ToString() == "530")
                throw new PrtgRequestException("Could not resolve the specified address; the PRTG map provider is not currently available");

            return null;
        }

#endregion
#if DEBUG
#pragma warning disable 1591
        [ExcludeFromCodeCoverage]
        internal bool UnitTest()
#pragma warning restore 1591
        {
            return Server == "prtg.example.com";
        }
#endif
    }
}
