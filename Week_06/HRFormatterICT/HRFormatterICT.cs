using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
// added in this version...
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;

// Students in Prof. McIntyre's Web Services course have permission to use this code as-is, and can modify it

namespace [your-project-name].ServiceLayer
{
    // Custom media formatter

    public class HRFormatterICT : JsonMediaTypeFormatter
    {
        public HRFormatterICT()
        {
            this.SupportedMediaTypes.Clear();
            this.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/json+ict"));
        }

        public override bool CanReadType(Type type)
        {
            return false;
        }

        public override bool CanWriteType(Type type)
        {
            return true;
        }

        public override void WriteToStream(Type type, object value, Stream writeStream, System.Text.Encoding content)
        {
            using (var writer = new StreamWriter(writeStream))
            {
                // First, create a package to hold the results
                var pkg = new ICTMediaType();

                // Then, get some information about the request
                // This depends upon the use of the default route template; in WebApiConfig.cs...
                // routeTemplate: "api/{controller}/{id}"
                // Also, we must remove any trailing slashes

                var request = HttpContext.Current.Request;

                var requestMethod = request.HttpMethod;
                var requestUri = request.Path.TrimEnd('/');
                var requestController = request.Url.Segments[2].TrimEnd('/');
                var requestId = (request.Url.Segments.Count() > 3) ? request.Url.Segments[3].TrimEnd('/') : "";

                // Next, discover what is in the response by inspecting the type name; it could be
                // 1. If error - return a package that holds the error message
                // 2. If collection - return a package that holds a collection
                // 3. If object - return a package that holds an object

                // Setup for switch-case statement
                string responseDataType = "";

                if (type.Name == "HttpError")
                {
                    responseDataType = "error";
                }
                else if (type.Namespace == "System.Collections.Generic")
                {
                    responseDataType = "collection";
                }
                else
                {
                    responseDataType = "object";
                }

                // This is the package generating code...

                switch (responseDataType)
                {
                    case "error":
                        {
                            // Link relation for "self"
                            pkg.links.Add(new link() { rel = "self", href = requestUri, methods = requestMethod });
                            pkg.count = 0;
                            pkg.data.Add(value);
                        }
                        break;

                    case "collection":
                        {
                            // How many items?
                            pkg.count = (value as ICollection).Count;

                            // Transform the data into an enumerable collection
                            IEnumerable collection = (IEnumerable)value;

                            foreach (var item in collection)
                            {
                                // Create an object that can expand at runtime 
                                // by dynamically and programmatically adding new properties
                                IDictionary<string, object> newItem = new ExpandoObject();

                                // N O T I C E 
                                // ###########

                                // We must do special processing for the Chinook database
                                // Each entity class has an identifier with a composite name,
                                // "Entity" name plus "Id" (e.g. CustomerId)
                                // We want to locate this "Entity" plus "Id" property name
                                // so that we can get the integer identifier for the item in the collection

                                // Algorithm... 
                                // 1. For each property (except "Id"), add it to newItem
                                // 2. While looking at each property, 
                                //    check whether its name matches the "Entity" plus "Id" pattern
                                // 3. If yes, save its value as the item's identifier 

                                bool shouldContinueLooking = true;

                                // Id value as an integer
                                int idValueInt = 0;

                                // Go through the all the properties in an item 
                                // and add them to the expando object
                                foreach (PropertyInfo prop in item.GetType().GetProperties())
                                {
                                    // Add the property
                                    newItem.Add(ConfigurePropertyName(prop), prop.GetValue(item));

                                    if (prop.Name == "Id")
                                    {
                                        // Save/remember the Id value
                                        idValueInt = (int)prop.GetValue(item);

                                        // Stop looking, because we have found the identifier
                                        shouldContinueLooking = false;
                                    }

                                    // Should we continue looking for the identifier?
                                    if (shouldContinueLooking)
                                    {
                                        // Setup the entity name
                                        var entityName = "";

                                        // Get the controller name
                                        // Remove the plural "s", if present
                                        var possibleBaseType = requestController.TrimEnd('s');

                                        // Compare the result against a number of rules/checks
                                        if (prop.Name.Length > 2 &&
                                            prop.Name.EndsWith("Id") &&
                                            prop.Name.StartsWith(possibleBaseType, true, null) &&
                                            prop.GetValue(item) is Int32)
                                        {
                                            // Boom, we have located the identifier
                                            entityName = possibleBaseType;
                                        }

                                        // Now do the comparison, if a match, add an "Id" property
                                        entityName = entityName + "Id";
                                        if (prop.Name.ToLower() == entityName.ToLower())
                                        {
                                            // We have found the identifier
                                            idValueInt = (int)prop.GetValue(item);
                                        }
                                    }
                                }

                                // Add the links (below)
                                dynamic o = item;

                                // ################################################################################
                                // Get the supported resource URIs for the item...
                                var allApiDescriptionsForItem = ApiExplorerService.GetApiDescriptionsForUri(requestController, idValueInt.ToString());

                                // Setup a collection to hold the links
                                var itemLinks = new List<link>();

                                // For each supported resource URI, generate and compose the link
                                foreach (var apiDescription in allApiDescriptionsForItem)
                                {
                                    // Fix the URI string
                                    var itemUri = apiDescription.RelativePath.Replace("{id}", idValueInt.ToString());

                                    // Create and initially configure the link
                                    var relValue = "self";
                                    if (apiDescription.HttpMethod.Method == "PUT" || apiDescription.HttpMethod.Method == "DELETE")
                                    {
                                        relValue = "edit";
                                    }
                                    var newLink = new link() { rel = relValue, href = itemUri, methods = apiDescription.HttpMethod.Method };
                                    newLink.title = apiDescription.Documentation;

                                    // Get the ActionDescriptor property
                                    var actionDescriptor = apiDescription.ActionDescriptor as System.Web.Http.Controllers.ReflectedHttpActionDescriptor;

                                    // Look for a "from body" parameter - we need that to render the field list
                                    var parBind = actionDescriptor.ActionBinding.ParameterBindings.SingleOrDefault(pb => pb.WillReadBody);

                                    // Generate the field list, if we have a parameter binding
                                    if (parBind != null)
                                    {
                                        // Get its data type
                                        Type parType = parBind.Descriptor.ParameterType;

                                        // Setup a fields collection 
                                        var fields = new List<field>();

                                        // Generate the field list (different procedure for strings)
                                        if (parType != null)
                                        {
                                            if (parType.Name == "String")
                                            {
                                                // Generate our own field
                                                fields.Add(new field { name = "(none)", type = "string" });
                                            }
                                            else
                                            {
                                                fields = GenerateFields(parType);
                                            }
                                        }

                                        newLink.fields = fields;
                                    }

                                    // Add the new link to the collection of links
                                    itemLinks.Add(newLink);
                                }

                                // Add the collection of links to the new item
                                newItem.Add("links", itemLinks);

                                // Add the new item to the package's "data" property
                                pkg.data.Add(newItem);
                            }

                            // ################################################################################
                            // Generate links for the collection URI

                            var allApiDescriptionsForColl = ApiExplorerService
                                .GetApiDescriptionsForUri(requestController, null);

                            // Setup a collection to hold the links
                            var pkgLinks = new List<link>();

                            // For each supported resource URI, generate and compose the link
                            foreach (var apiDescription in allApiDescriptionsForColl)
                            {
                                // Fix the URI string
                                var itemUri = apiDescription.RelativePath.Replace("{id}", 0.ToString());

                                // Create and initially configure the link
                                var relValue = "self";
                                if (apiDescription.HttpMethod.Method == "POST") { relValue = "edit"; }
                                var newLink = new link() { rel = relValue, href = itemUri, methods = apiDescription.HttpMethod.Method };
                                newLink.title = apiDescription.Documentation;

                                // Get the ActionDescriptor property
                                var actionDescriptor = apiDescription.ActionDescriptor as System.Web.Http.Controllers.ReflectedHttpActionDescriptor;

                                Type parType = null;

                                // Look for a "from body" parameter - we need that to render the field list
                                var parBind = actionDescriptor.ActionBinding.ParameterBindings.SingleOrDefault(pb => pb.WillReadBody);
                                if (parBind != null)
                                {
                                    parType = parBind.Descriptor.ParameterType;
                                }

                                // Alternatively, look for a "from URI" parameter, as we can use that too
                                if (apiDescription.ParameterDescriptions.Count == 1)
                                {
                                    var pd = apiDescription.ParameterDescriptions[0];
                                    if (pd.Source == System.Web.Http.Description.ApiParameterSource.FromUri)
                                    {
                                        // Yay, can use the binding and the type
                                        parBind = actionDescriptor.ActionBinding.ParameterBindings[0];
                                        parType = pd.ParameterDescriptor.ParameterType;
                                    }
                                }

                                // Generate the field list, if we have a parameter binding
                                if (parBind != null)
                                {
                                    // Get its data type
                                    //Type parType = parBind.Descriptor.ParameterType;

                                    // Setup a fields collection 
                                    var fields = new List<field>();

                                    // Generate the field list (different procedure for strings)
                                    if (parType != null)
                                    {
                                        if (parType.Name == "String")
                                        {
                                            // Generate our own field
                                            fields.Add(new field { name = "(none)", type = "string" });
                                        }
                                        else
                                        {
                                            fields = GenerateFields(parType);
                                        }
                                    }

                                    newLink.fields = fields;
                                }

                                // Add the new link to the collection of links
                                pkgLinks.Add(newLink);
                            }

                            // Add the collection of links to the package
                            pkg.links = pkgLinks;

                        }
                        break;

                    case "object":
                        {
                            // No, NOT a collection

                            IDictionary<string, object> newItem = new ExpandoObject();

                            // Go through the all the properties in an item
                            foreach (PropertyInfo prop in value.GetType().GetProperties())
                            {
                                newItem.Add(ConfigurePropertyName(prop), prop.GetValue(value));
                            }

                            pkg.count = 1;
                            pkg.data.Add(newItem);

                            // ################################################################################
                            // Get the supported resource URIs for the item...
                            var allApiDescriptionsForItem = ApiExplorerService.GetApiDescriptionsForUri(requestController, requestId.ToString());

                            // Setup a collection to hold the links
                            var itemLinks = new List<link>();

                            // For each supported resource URI, generate and compose the link
                            foreach (var apiDescription in allApiDescriptionsForItem)
                            {
                                // Fix the URI string
                                var itemUri = apiDescription.RelativePath.Replace("{id}", requestId.ToString());

                                // Create and initially configure the link
                                var relValue = "self";
                                if (apiDescription.HttpMethod.Method == "PUT" || apiDescription.HttpMethod.Method == "DELETE")
                                {
                                    relValue = "edit";
                                }
                                var newLink = new link() { rel = relValue, href = itemUri, methods = apiDescription.HttpMethod.Method };
                                newLink.title = apiDescription.Documentation;

                                // Get the ActionDescriptor property
                                var actionDescriptor = apiDescription.ActionDescriptor as System.Web.Http.Controllers.ReflectedHttpActionDescriptor;

                                // Look for a "from body" parameter - we need that to render the field list
                                var parBind = actionDescriptor.ActionBinding.ParameterBindings.SingleOrDefault(pb => pb.WillReadBody);

                                // Generate the field list, if we have a parameter binding
                                if (parBind != null)
                                {
                                    // Get its data type
                                    Type parType = parBind.Descriptor.ParameterType;

                                    // Setup a fields collection 
                                    var fields = new List<field>();

                                    // Generate the field list (different procedure for strings)
                                    if (parType != null)
                                    {
                                        if (parType.Name == "String")
                                        {
                                            // Generate our own field
                                            fields.Add(new field { name = "(none)", type = "string" });
                                        }
                                        else
                                        {
                                            fields = GenerateFields(parType);
                                        }
                                    }

                                    newLink.fields = fields;
                                }

                                // Add the new link to the collection of links
                                itemLinks.Add(newLink);
                            }

                            pkg.links = itemLinks;

                            // ################################################################################
                            // Generate links for the collection URI

                            var allApiDescriptionsForColl = ApiExplorerService
                                .GetApiDescriptionsForUri(requestController, null);

                            // Setup a collection to hold the links
                            var pkgLinks = new List<link>();

                            // For each supported resource URI, generate and compose the link
                            foreach (var apiDescription in allApiDescriptionsForColl)
                            {
                                // Fix the URI string
                                //var itemUri = apiDescription.RelativePath.Replace("{id}", idValueInt.ToString());
                                var itemUri = apiDescription.RelativePath.Replace("{id}", 0.ToString());

                                // Create and initially configure the link
                                var relValue = "collection";
                                if (apiDescription.HttpMethod.Method == "POST") { relValue = "edit"; }
                                var newLink = new link() { rel = relValue, href = itemUri, methods = apiDescription.HttpMethod.Method };
                                newLink.title = apiDescription.Documentation;

                                // Get the ActionDescriptor property
                                var actionDescriptor = apiDescription.ActionDescriptor as System.Web.Http.Controllers.ReflectedHttpActionDescriptor;

                                Type parType = null;

                                // Look for a "from body" parameter - we need that to render the field list
                                var parBind = actionDescriptor.ActionBinding.ParameterBindings.SingleOrDefault(pb => pb.WillReadBody);
                                if (parBind != null)
                                {
                                    parType = parBind.Descriptor.ParameterType;
                                }

                                // Alternatively, look for a "from URI" parameter, as we can use that too
                                if (apiDescription.ParameterDescriptions.Count == 1)
                                {
                                    var pd = apiDescription.ParameterDescriptions[0];
                                    if (pd.Source == System.Web.Http.Description.ApiParameterSource.FromUri)
                                    {
                                        // Yay, can use the binding and the type
                                        parBind = actionDescriptor.ActionBinding.ParameterBindings[0];
                                        parType = pd.ParameterDescriptor.ParameterType;
                                    }
                                }

                                // Generate the field list, if we have a parameter binding
                                if (parBind != null)
                                {
                                    // Get its data type
                                    //Type parType = parBind.Descriptor.ParameterType;

                                    // Setup a fields collection 
                                    var fields = new List<field>();

                                    // Generate the field list (different procedure for strings)
                                    if (parType != null)
                                    {
                                        if (parType.Name == "String")
                                        {
                                            // Generate our own field
                                            fields.Add(new field { name = "(none)", type = "string" });
                                        }
                                        else
                                        {
                                            fields = GenerateFields(parType);
                                        }
                                    }

                                    newLink.fields = fields;
                                }

                                // Add the new link to the collection of links
                                pkgLinks.Add(newLink);
                            }

                            // Add the collection of links to the package
                            pkg.links.AddRange(pkgLinks);


                        }
                        break;

                    default:
                        break;
                }

                // Deliver the package...

                string json = JsonConvert.SerializeObject(pkg, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
                var buffer = Encoding.Default.GetBytes(json);
                writeStream.Write(buffer, 0, buffer.Length);
                writeStream.Flush();
                writeStream.Close();
            }
        }

        /// <summary>
        /// Generate the fields for a resource URI
        /// </summary>
        /// <param name="parType">Type of the object that will be inspected</param>
        /// <returns>Collection of field objects</returns>
        private List<field> GenerateFields(Type parType)
        {
            // Setup a collection to hold the type's fields
            var fields = new List<field>();

            // Now, go through all the properties in the class/type
            foreach (var propInfo in parType.GetProperties())
            {
                string description = "(none)";
                string usage = "";
                foreach (var customAttribute in propInfo.CustomAttributes)
                {
                    // Look for custom attributes 
                    // (data annotations, Required, StringLength (max and min)...)
                    var attrName = customAttribute.AttributeType.Name;
                    var maxValue = customAttribute.ConstructorArguments.Count > 0 ? customAttribute.ConstructorArguments[0].Value.ToString() : "";
                    var minValue = "";

                    // Look for named arguments
                    CustomAttributeNamedArgument minArg;

                    if (customAttribute.NamedArguments.Count > 0)
                    {
                        if (customAttribute.NamedArguments[0].MemberName == "MinimumLength")
                        {
                            minArg = customAttribute.NamedArguments[0];
                            minValue = customAttribute.NamedArguments[0].TypedValue.Value.ToString();
                        }
                    }

                    // Clean up the strings

                    if (attrName == "RangeAttribute")
                    {
                        minValue = customAttribute.ConstructorArguments[0].Value.ToString();
                        maxValue = customAttribute.ConstructorArguments[1].Value.ToString();
                        usage = $"Value is from {minValue} to {maxValue}. ";
                    }

                    if (attrName == "RequiredAttribute")
                    {
                        usage = "Required. ";
                    }

                    if (attrName == "DescriptionAttribute")
                    {
                        description = maxValue;
                    }

                    if (attrName == "StringLengthAttribute")
                    {
                        usage = $"{usage}Maximum length is {maxValue}. ";

                        if (minValue.Length > 0)
                        {
                            usage = $"{usage}Minimum length is {minValue}. ";
                        }
                    }
                }

                if (string.IsNullOrEmpty(usage)) { usage = "(none)"; }

                // Type cleanup; convert .NET Framework names to generic names
                var propType = propInfo.PropertyType.Name;
                var fieldType = propType;

                if (propType == "Int32") { fieldType = "integer"; }
                if (propType == "Nullable`1") { fieldType = "integer"; }
                if (propType == "DateTime") { fieldType = "ISO 8601 date and time"; }

                // Finally, add the field
                fields.Add(new field
                {
                    name = ConfigurePropertyName(propInfo),
                    type = fieldType.ToLower(),
                    description = description,
                    usage = usage
                });

            }

            // All done, return the collection of fields
            return fields;
        }

        /// <summary>
        /// Look for and configure a custom property name
        /// </summary>
        /// <param name="prop">Property information object</param>
        /// <returns>String that is set with a DisplayAttribute</returns>
        private string ConfigurePropertyName(PropertyInfo prop)
        {
            var customAttr = prop.CustomAttributes.SingleOrDefault(ca => ca.AttributeType.Name == "DisplayAttribute");
            if (customAttr == null)
            {
                return prop.Name;
            }
            else
            {
                return customAttr.NamedArguments[0].TypedValue.Value.ToString();
            }
        }

    }


    // API explorer service, programmatically inspects a controller
    // to get information on what HTTP methods are supported by its methods

    // Shout out to Jef Claes for the inspiration and design
    // http://www.jefclaes.be/2012/09/supporting-options-verb-in-aspnet-web.html

    public class ApiExplorerService
    {
        // Get supported ApiDescriptions for item or collection URI
        public static IEnumerable<System.Web.Http.Description.ApiDescription> GetApiDescriptionsForUri(string controllerRequested, string idRequested)
        {
            // Get a reference to the API Explorer
            var apiExplorer = GlobalConfiguration.Configuration.Services.GetApiExplorer();

            // Collector for the supported methods
            IEnumerable<System.Web.Http.Description.ApiDescription> supportedApiDescriptions = null;

            supportedApiDescriptions = apiExplorer.ApiDescriptions.Where(d =>
            {
                // In the controller class, look for methods that match
                // the requested controller name and the presence of an id parameter
                var controller = d.ActionDescriptor.ControllerDescriptor.ControllerName;
                var idParameter = d.ParameterDescriptions.SingleOrDefault(p => p.Name == "id");
                bool doesControllerMatch = string.Equals(controller, (string)controllerRequested, StringComparison.OrdinalIgnoreCase);
                bool hasId = (idParameter == null) ? false : true;

                if (idRequested == null)
                {
                    // We want collection data
                    return doesControllerMatch & !hasId;
                }
                else
                {
                    // We want item data
                    return doesControllerMatch & hasId;
                }

            });

            return supportedApiDescriptions;
        }

        public static IEnumerable<string> GetSupportedMethods(string controllerRequested, string idRequested)
        {
            // Get a reference to the API Explorer
            var apiExplorer = GlobalConfiguration.Configuration.Services.GetApiExplorer();

            // Three possible situations for the URI path...
            // 1. Controller, no id -       /api/items
            // 2. Controller, id -          /api/items/3
            // 3. No controller, no id -    /api

            // Collector for the supported methods
            IEnumerable<string> supportedMethods = null;

            if (string.IsNullOrEmpty(idRequested))
            {
                // 1. Controller, no id -       /api/items
                // ##################################################

                supportedMethods = apiExplorer.ApiDescriptions.Where(d =>
                {
                    // In the controller class, look for methods that match
                    // the requested controller name and nothing for the id parameter
                    var controller = d.ActionDescriptor.ControllerDescriptor.ControllerName;
                    var idParameter = d.ParameterDescriptions.SingleOrDefault(p => p.Name == "id");
                    bool doesControllerMatch = string.Equals(controller, (string)controllerRequested, StringComparison.OrdinalIgnoreCase);
                    bool isIdNull = (idParameter == null) ? true : false;
                    return doesControllerMatch & isIdNull;
                })
                .Select(d => d.HttpMethod.Method)
                .Distinct();
            }
            else
            {
                // 2. Controller, id -          /api/items/3
                // ##################################################

                supportedMethods = apiExplorer.ApiDescriptions.Where(d =>
                {
                    // In the controller class, look for methods that match
                    // the requested controller name and the presence of an id parameter
                    var controller = d.ActionDescriptor.ControllerDescriptor.ControllerName;
                    var idParameter = d.ParameterDescriptions.SingleOrDefault(p => p.Name == "id");
                    bool doesControllerMatch = string.Equals(controller, (string)controllerRequested, StringComparison.OrdinalIgnoreCase);
                    bool hasId = (idParameter == null) ? false : true;
                    return doesControllerMatch & hasId;
                })
                .Select(d => d.HttpMethod.Method)
                .Distinct();
            }

            // 3. No controller, no id -    /api
            // ##################################################

            if (string.IsNullOrEmpty((string)controllerRequested))
            {
                supportedMethods = apiExplorer.ApiDescriptions.Where(d =>
                {
                    // In the RootController class, look for matching methods
                    var controller = d.ActionDescriptor.ControllerDescriptor.ControllerName;
                    return string.Equals(controller, "root", StringComparison.OrdinalIgnoreCase);
                })
                    .Select(d => d.HttpMethod.Method)
                    .Distinct();
            }

            return supportedMethods;
        }
    }

    // HTTP OPTIONS handler

    // Add the following to the Register method body in the WebApiConfig class
    //// Handle HTTP OPTIONS requests
    //config.MessageHandlers.Add(new ServiceLayer.HandleHttpOptions());

    public class HandleHttpOptions : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Options)
            {
                // Get the controller and id values
                var controllerRequested = request.GetRouteData().Values["controller"] as string;
                var idRequested = request.GetRouteData().Values["id"] as string;

                // Collector for the supported methods
                IEnumerable<string> supportedMethods = ApiExplorerService.GetSupportedMethods(controllerRequested, idRequested);

                // The controllerRequested does not exist, so return HTTP 404
                if (!supportedMethods.Any())
                {
                    return Task.Factory.StartNew(() => request.CreateResponse(HttpStatusCode.NotFound));
                }

                return Task.Factory.StartNew(() =>
                {
                    var resp = new HttpResponseMessage(HttpStatusCode.OK);
                    string methods = string.Join(",", supportedMethods);
                    // For standard requests, add the 'Allow' header
                    resp.Content = new StringContent("");
                    resp.Content.Headers.Add("Allow", methods);
                    // For Ajax requests
                    resp.Headers.Add("Access-Control-Allow-Origin", "*");
                    resp.Headers.Add("Access-Control-Allow-Methods", methods);

                    return resp;
                });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    // This is an example of a hypermedia representation
    // We will adapt this to its final form next week

    public class ICTMediaType
    {
        public ICTMediaType()
        {
            timestamp = DateTime.Now;
            count = 0;
            version = "1.0.0";
            data = new List<dynamic>();
            links = new List<link>();
        }
        public DateTime timestamp { get; set; }
        public string version { get; set; }
        public int count { get; set; }
        public ICollection<dynamic> data { get; set; }
        public List<link> links { get; set; }
    }

    // This is a "link" class that describes a link relation

    // All symbols (property names) are lower-case, to conform to web standards

    /// <summary>
    /// A hypermedia link
    /// </summary>
    public class link
    {
        /// <summary>
        /// Relation kind
        /// </summary>
        // You configure this value
        public string rel { get; set; } = "";

        /// <summary>
        /// Hypermedia reference URL segment
        /// </summary>
        // You configure this value
        public string href { get; set; } = "";

        // New added properties...

        // The null value handling issue is controversial
        // Attributes were used here to make the result look nicer (without null-valued properties)
        // However, read these...
        // StackOverflow - http://stackoverflow.com/questions/10150312/removing-null-properties-from-json-in-mvc-web-api-4-beta
        // CodePlex - http://aspnetwebstack.codeplex.com/workitem/243

        /// <summary>
        /// Internet media type, for content negotiation
        /// </summary>
        // This value is configured by the ApiExplorer
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string type { get; set; }

        /// <summary>
        /// HTTP method(s) which can be used
        /// </summary>
        // This value is configured by the ApiExplorer
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string methods { get; set; }

        /// <summary>
        /// Human-readable title label
        /// </summary>
        // This value is configured by an XML Documentation Comment on the controller method
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string title { get; set; }

        /// <summary>
        /// Values which must be sent with the request
        /// </summary>
        // We do NOT want to initialize the value of this property in the constructor
        // If it is truly null, then we do not want it to be rendered
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<field> fields { get; set; }
    }

    public class field
    {
        /// <summary>
        /// Name of the field
        /// </summary>
        // This value is from the resource model class
        // It is the property name, or the value of the DisplayAttribute
        public string name { get; set; } = "";

        /// <summary>
        /// Data type of the field
        /// </summary>
        // This value is configured by the ApiExplorer
        public string type { get; set; } = "";

        /// <summary>
        /// Initial value of the field (if available)
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string value { get; set; }

        /// <summary>
        /// Description, purpose
        /// </summary>
        // This value is from the resource model class
        // It is the value of the DescriptionAttribute
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string description { get; set; }

        /// <summary>
        /// How to use, constraints, considerations
        /// </summary>
        // This value is configured by the ApiExplorer
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string usage { get; set; }
    }

}