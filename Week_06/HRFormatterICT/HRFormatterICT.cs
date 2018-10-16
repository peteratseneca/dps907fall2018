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

namespace [your-project-name].ServiceLayer
{
    // Custom media formatter

    public class HRFormatterICT : JsonMediaTypeFormatter
    {
        public HRFormatterICT()
        {
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

                if (value != null)
                {
                    // Determine the pattern by gathering query characteristics

                    // How many segments, after the fixed "/api/ segments
                    // ==================================================

                    // Will always have something in it
                    var segments = HttpContext.Current.Request.Url.Segments;
                    // Remove the first two segments, / and api/
                    // This will leave only the controller name and whatever follows that
                    var segmentsCount = segments.Length - 2;

                    // Do we have a query string?
                    // ==========================

                    var query = HttpContext.Current.Request.QueryString;
                    // If there's no query string, it does not blow up
                    var queryCount = query.Count;
                    // If there's no query string, this value is zero

                    // Get the route data, look for integer "id" property
                    // ==================================================

                    HttpRequestMessage hrm = HttpContext.Current.Items["MS_HttpRequestMessage"] as HttpRequestMessage;
                    var routeData = hrm.GetRouteData();
                    // Has route template as a string
                    // Also has or shows "id" as a "parameter"

                    var rdItem = routeData.Values.SingleOrDefault(r => r.Key == "id");
                    // We'll get back a kvp with the data, or with nulls for key and value

                    var idKeyValue = Convert.ToInt32(rdItem.Value);
                    // If this is zero, then "id" is not in the route data
                    // If non-zero, "id" is in the route data, and we have the value

                    // Do we have an integer identifier?
                    var intId = (!string.IsNullOrEmpty(rdItem.Key)) ? true : false;

                    // How many items are in the response?
                    // ===================================

                    //var isCollection =
                    //    value.GetType().GetInterfaces()
                    //    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

                    var isCollection = type.Namespace == "System.Collections.Generic" ? true : false;

                    var itemCount = 1;

                    if (isCollection)
                    {
                        var items = (ICollection)value;
                        itemCount = items.Count;
                    }

                    // Continue...

                    var absolutePath = HttpContext.Current.Request.Url.AbsolutePath;
                    string[] u = absolutePath.Split(new char[] { '/' });

                    // We will use this later (soon)...
                    var pattern = 0;

                    if (isCollection)
                    {
                        // Will have zero or more items
                        // Will either be a get-all
                        // Or a get-some-filtered

                        // Come back and fix this later...
                        pattern = -1;

                        if (segmentsCount == 1 && queryCount == 0 && idKeyValue == 0)
                        {
                            // Get all
                            // Zero or more items in "value"
                            // Exactly 1 segment after "/api/", which suggests a collection
                            // Does not have a query string 
                            // Does NOT have the "id" parameter in the route
                            pattern = 1;
                        }

                        if (segmentsCount == 1 && queryCount > 0 && idKeyValue == 0)
                        {
                            // Get some filtered
                            // Zero or more items in "value"
                            // Exactly 1 segment after "/api/", which suggests a collection
                            // Has a query string 
                            // Does NOT have the "id" parameter in the route
                            pattern = 2;
                        }

                        IEnumerable collection = (IEnumerable)value;
                        int count = 0;
                        foreach (var item in collection)
                        {
                            count++;
                            IDictionary<string, object> newItem = new ExpandoObject();

                            // Name and value of the field that holds the identifier
                            var baseClassName = "";
                            var idValue = 0;

                            // Go through the all the properties in an item
                            foreach (PropertyInfo prop in item.GetType().GetProperties())
                            {
                                // N O T I C E 
                                // ###########

                                // Special processing for the Chinook database
                                // Each entity class has an identifier with a composite name
                                // Entity plus Id (e.g. CustomerId)

                                // Algorithm...
                                // 1. For each property (except "Id"), add it to newItem
                                // 2. While looking at each property, 
                                //    check whether its name matches the "Entity" plus "Id" pattern
                                // 3. If yes, create a property named "Id" with the same value

                                // Safety check, which rejects any property named "Id"
                                if (!(prop.Name == "Id"))
                                {
                                    newItem.Add(prop.Name, prop.GetValue(item));
                                }

                                // New algorithm...

                                var objName = "";

                                // Get the all-but-last character of the URI segment for the "controller"
                                var possibleBaseType = u[2];

                                // Remove the plural "s", if present
                                if (u[2].EndsWith("s", false, null))
                                {
                                    possibleBaseType = u[2].TrimEnd('s');
                                }

                                // Compare the result against a number of rules/checks
                                if (prop.Name.Length > 2 &&
                                    prop.Name.EndsWith("Id") &&
                                    prop.Name.StartsWith(possibleBaseType, true, null) &&
                                    prop.GetValue(item) is Int32)
                                {
                                    // Boom, we have located the identifier
                                    objName = possibleBaseType;
                                }

                                // We now have the name of the base class 
                                // (but do we need it for anything?)
                                baseClassName = objName;

                                // Now do the comparison, if a match, add an "Id" property
                                objName = objName + "Id";
                                if (prop.Name.ToLower() == objName.ToLower())
                                {
                                    newItem.Add("Id", prop.GetValue(item));
                                    // Save the value of this identifier to make the link next/below
                                    idValue = (int)prop.GetValue(item);
                                }
                            }

                            // Add the links (below)
                            dynamic o = item;

                            // Get the supported HTTP methods for the item...

                            // Setup...
                            object idValueObject;
                            int idValueInt = 0;
                            if (newItem.TryGetValue("Id", out idValueObject))
                            {
                                idValueInt = (int)idValueObject;
                            }

                            // Get the item methods and add a link
                            var itemMethods = string.Join(",", ApiExplorerService.GetSupportedMethods(u[2], idValueInt.ToString()));
                            newItem.Add("Link", new link() { rel = "item", href = string.Format("{0}/{1}", absolutePath, idValueInt), methods = itemMethods });

                            pkg.data.Add(newItem);
                        }

                        // Add a link relation for 'self'
                        pkg.links.Add(new link() { rel = "self", href = absolutePath, methods = "GET" });

                        // Link relation for 'create', if supported
                        // Hard-coded for now - we want to make this discoverable

                        // TODO - make this discoverable ! ! ! 

                        if (ApiExplorerService.GetSupportedMethods(u[2], null).Contains("POST"))
                        {
                            var postLink = new link() { rel = "create", href = absolutePath, methods = "POST" };
                            postLink.fields = new List<field>();
                            postLink.fields.Add(new field { name = "FirstName", type = "string" });
                            postLink.fields.Add(new field { name = "LastName", type = "string" });
                            postLink.fields.Add(new field { name = "Age", type = "int" });

                            pkg.links.Add(postLink);
                        }

                        pkg.count = count;
                    }
                    else
                    {
                        // No, NOT a collection

                        // Set pattern (come back to this later and fix)
                        pattern = -1;

                        IDictionary<string, object> newItem = new ExpandoObject();

                        // Go through the all the properties in an item
                        foreach (PropertyInfo prop in value.GetType().GetProperties())
                        {
                            newItem.Add(prop.Name, prop.GetValue(value));
                        }

                        var itemMethods = string.Join(",", ApiExplorerService.GetSupportedMethods(u[2], u[3]));
                        newItem.Add("Link", new link() { rel = "self", href = absolutePath, methods = itemMethods });

                        // Link relation for 'self'
                        pkg.links.Add(new link() { rel = "self", href = absolutePath, methods = itemMethods });

                        var controllerMethods = string.Join(",", ApiExplorerService.GetSupportedMethods(u[2], null));

                        // Link relation for 'collection'
                        pkg.links.Add(new link() { rel = "collection", href = string.Format("/{0}", u[1]), methods = controllerMethods });

                        pkg.count = 1;
                        pkg.data.Add(newItem);
                    }
                }

                string json = JsonConvert.SerializeObject(pkg, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
                var buffer = Encoding.Default.GetBytes(json);
                writeStream.Write(buffer, 0, buffer.Length);
                writeStream.Flush();
                writeStream.Close();
            }
        }
    }


    // API explorer service, programmatically inspects a controller
    // to get information on what HTTP methods are supported by its methods

    // Shout out to Jef Claes for the inspiration and design
    // http://www.jefclaes.be/2012/09/supporting-options-verb-in-aspnet-web.html

    // Students in Prof. McIntyre's Web Services course have permission to use this code as-is

    public class ApiExplorerService
    {
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

    // All symbols are lower-case, to conform to web standards

    /// <summary>
    /// A hypermedia link
    /// </summary>
    public class link
    {
        public link()
        {
            //fields = new List<field>();
        }

        /// <summary>
        /// Relation kind
        /// </summary>
        public string rel { get; set; } = "";

        /// <summary>
        /// Hypermedia reference URL segment
        /// </summary>
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
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string type { get; set; }

        /// <summary>
        /// HTTP method(s) which can be used
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string methods { get; set; }

        /// <summary>
        /// Human-readable title label
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string title { get; set; }

        /// <summary>
        /// Values which must be sent with the request
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<field> fields { get; set; }
    }

    public class field
    {
        /// <summary>
        /// Name of the field
        /// </summary>
        public string name { get; set; } = "";

        /// <summary>
        /// Data type of the field
        /// </summary>
        public string type { get; set; } = "";

        /// <summary>
        /// Initial value of the field (if available)
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string value { get; set; }
    }

}