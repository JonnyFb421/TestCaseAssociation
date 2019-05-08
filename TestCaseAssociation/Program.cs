using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;
using TestCaseAttributes.TestCaseId;
using Xunit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace TestCaseAssociation
{
    class Program
    {
        public static List<string> AssociationErrors = new List<string>();
        private static string PersonalAccessToken = Environment.GetEnvironmentVariable("AZURE_TOKEN");
        private static string AzureHost;
        private static string AzureProject;
        private static string AutomatedTestType;
        private static string AutomatedTestDllName;
        private static string MaxMissingTestCases;

        static void Main(string[] args)
        {
            Console.WriteLine("Startinging test case association");
            AzureHost = args[0];
            AzureProject = args[1];
            AutomatedTestType = args[2];
            AutomatedTestDllName = args[3];
            MaxMissingTestCases = args[4];
            bool DryRun = bool.Parse(args[5]);

            ValidateEnvironmentVariables();

            Console.WriteLine("Getting known test cases from Azure");
            var knownAssociatedTestCaseIds = GetKnownAssociationsFromAzure();

            // Load assembly and get test methods from all types
            Console.WriteLine($"Finding all test methods from {AutomatedTestDllName}");
            string[] files = Directory.GetFiles(Environment.GetEnvironmentVariable("Build_SourcesDirectory"), AutomatedTestDllName, SearchOption.AllDirectories);
            string pathToAssembly = files.First();
            Assembly targetAssembly = Assembly.LoadFrom(pathToAssembly);

            Type[] allTypesInThisAssembly = targetAssembly.GetTypes();
            List<MethodInfo> validTestCases = allTypesInThisAssembly
                .SelectMany(x => x.GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(FactAttribute), true).Length > 0 ||
                            m.GetCustomAttributes(typeof(TheoryAttribute), true).Length > 0)).ToList();

            // Creates dictionary containing `test method name: test case id`
            Console.WriteLine($"Getting TestCaseId attributes from test methods");
            Dictionary<string, string> validAssociations = ProcessTestMethods(validTestCases);


            // Test Case Validation
            Console.WriteLine($"Validating TestCaseIds");
            CheckForMissingTestCaseAssociations(validAssociations);
            CheckForDuplicateTestCaseIds(validAssociations);
            CheckForInvalidTestCaseIds(validAssociations);

            // Create association in Azure Devops
            if (DryRun == false)
            {
                Console.WriteLine("Reaching out to Azure to create new test case associations");
                AddTestCaseLinkToVSTSTest(validAssociations);
                var newAssociatedTestCaseIds = GetKnownAssociationsFromAzure();
                var deltas = GetOrphanedTestCaseIds(knownAssociatedTestCaseIds, newAssociatedTestCaseIds);
                foreach (var delta in deltas)
                {
                    DissociateTestCaseLinkage(delta);
                    Console.WriteLine($"Cleared automation fields from Azure Test Case ID {delta}");
                }
            }
        }

        private static void ValidateEnvironmentVariables()
        {
            string error_message;
            if (AzureHost == null)
            {
                error_message =
                    "Missing required configuration setting: AZURE_HOST." +
                    "\nExample: https://goatwranglers.visualstudio.com";
                throw new System.Exception(error_message);
            }
            if (AzureProject == null)
            {
                error_message =
                    "Missing required configuration setting: AZURE_PROJECT." +
                    "\nExample: GWrangler";
                throw new System.Exception(error_message);
            }
            if (PersonalAccessToken == null)
            {
                error_message =
                    "Missing required configuration setting: AZURE_TOKEN." +
                    "\nExample: hujikm4324uhyybhi112dsfasfdsaf3424jioij2dsfadfsdafds";
                throw new System.Exception(error_message);
            }
            if (AutomatedTestType == null)
            {
                error_message =
                    "Missing required configuration setting: TEST_TYPE." +
                    "\nExample: UI";
                throw new System.Exception(error_message);
            }
            if (AutomatedTestDllName == null)
            {
                error_message =
                    "Missing required configuration setting: TEST_DLL." +
                    "\nExample: MyProject.dll";
                throw new System.Exception(error_message);
            }


        }

        private static void CheckForInvalidTestCaseIds(Dictionary<string, string> validAssociations)
        {
    
            List<string> testCaseIds = new List<string>();
            foreach (var testCase in validAssociations)
            {
                testCaseIds.Add(testCase.Value);
            }
            var invalidTestCases = VerifyIdBelongsToAzureTestCaseType(testCaseIds);
            if (invalidTestCases.Any())
            {
                string error_type = "\nERROR: Test Case Id does not belong to an Azure Devops Test Case: ";
                string error_message = $"{error_type}{String.Join(error_type, invalidTestCases)}";
                throw new System.Exception(error_message);
            }
        }

        private static void CheckForDuplicateTestCaseIds(Dictionary<string, string> validAssociations)
        {
            if (HasDuplicateIds(validAssociations))
            {
                var duplicateTestIds = GetDuplicateTestCaseIds(validAssociations);
                string error_type = "\nERROR: Test Case Id duplication detected: ";
                string error_message = $"{error_type}{String.Join(error_type, duplicateTestIds)}";
                throw new System.Exception(error_message);
            }

            ;
        }

        private static void CheckForMissingTestCaseAssociations(Dictionary<string, string> validAssociations)
        {
            int maxMissingTestCasesThreshold;
            if (String.IsNullOrEmpty(MaxMissingTestCases))
            {
                maxMissingTestCasesThreshold = 0;
            }
            else
            {
                maxMissingTestCasesThreshold = int.Parse(MaxMissingTestCases);
            }

            if (ExceedsMaxMissingTestCases(maxMissingTestCasesThreshold))
            {
                string error_message = $"Failing build due to exceeding the threshold for unassociated test cases." +
                                       $"\nThe following tests missing the [TestCaseId] attribute:\n{string.Join("\n", AssociationErrors)}" +
                                       $"\nTotal valid test associations: {validAssociations.Count}" +
                                       $"\nTotal invalid test associations: {AssociationErrors.Count}" +
                                       $"\nMaximum number of allowed invalid associations: {maxMissingTestCasesThreshold}";
                throw new System.Exception(error_message);
            }
        }

        private static bool ExceedsMaxMissingTestCases(int maxMissingTestCases)
        {
            return AssociationErrors.Count > maxMissingTestCases;
        }


        private static bool HasDuplicateIds(Dictionary<string, string> validAssociations)
        {
            var testCaseIds = validAssociations.Values;
            bool containsDuplicates = testCaseIds.Distinct().Count() != testCaseIds.Count();
            return containsDuplicates;
        }


        private static List<string> GetDuplicateTestCaseIds(Dictionary<string, string> validAssociations)
        {
            List<string> duplicateIds = new List<string>();
            List<string> knownIds = new List<string>();
            foreach (KeyValuePair<string, string> kvp in validAssociations)
            {
                if (knownIds.Contains(kvp.Value))
                {
                    duplicateIds.Add(kvp.Value);
                }
                else
                {
                    knownIds.Add(kvp.Value);
                }
            }

            return duplicateIds;
        }

        private static List<string> GetOrphanedTestCaseIds(List<string> knownAssociations, List<string> newAssociations)
        {
            List<string> deltas = new List<string>();
            foreach (var association in knownAssociations)
            {
                if (!newAssociations.Contains(association))
                {
                    deltas.Add(association);
                }
            }
    
            return deltas;
        }

        private static JObject GetTestCaseWorkItemById(string testCaseId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        Encoding.ASCII.GetBytes(
                            string.Format("{0}:{1}", "", PersonalAccessToken))));
                string url = $"{AzureHost}/{AzureProject}/_apis/wit/workitems/{testCaseId}?api-version=5.0";

                HttpMethod method = new HttpMethod("GET");

                var request = new HttpRequestMessage(method, url);

                HttpResponseMessage response = new HttpResponseMessage();
                response = client.SendAsync(request).Result;

                response.EnsureSuccessStatusCode();
                JObject content = JObject.Parse(
                    response.Content.ReadAsStringAsync().Result
                );
                return content;
            }
        }


        private static JObject GetTestCaseWorkItemByIdWithWiql(string testCaseId)
        {
            dynamic azureQuery = new JObject();
            azureQuery.Query =
                $"Select [Id] From WorkItems Where [Work Item Type] = 'Test Case' AND [Id] = {testCaseId}";
            var response = SendWiqlToAzure(azureQuery);
            var jsonResult = JObject.Parse(
                response.Content.ReadAsStringAsync().Result
            );
            return jsonResult;
        }

        private static bool AutomatedFieldsNeedToChange(string testName, string testCaseId)
        {
            bool needToChange = true;
            JObject workItem = GetTestCaseWorkItemById(testCaseId);
            if (workItem != null && workItem["fields"]["Microsoft.VSTS.TCM.AutomatedTestName"].ToString() == testName)
            {
                needToChange = false;
            }
            return needToChange;
        }

        private static List<string> VerifyIdBelongsToAzureTestCaseType(List<string> testCaseIds)
        {
            List<string> invalidTestCaseIds = new List<string>();
            foreach (var testCaseId in testCaseIds)
            {
                JObject queryResults = GetTestCaseWorkItemByIdWithWiql(testCaseId);
                if (queryResults["workItems"].Count() == 0)
                {
                    invalidTestCaseIds.Add(testCaseId);
                }
            }

            return invalidTestCaseIds;
        }

        private static List<string> GetKnownAssociationsFromAzure()
        {
            List<string> knownAssociations = new List<string>();
            dynamic azureQuery = new JObject();
            azureQuery.Query =
                "Select [Id] From WorkItems Where [Work Item Type] = 'Test Case' AND [Automated test storage] = 'ArcFilingSpecs.dll' order by [Id] desc";
            var response = SendWiqlToAzure(azureQuery);
            var jsonResult = JObject.Parse(
                response.Content.ReadAsStringAsync().Result
            );

            var workItems = jsonResult["workItems"];
            foreach (var workItem in workItems)
            {
                knownAssociations.Add(workItem["id"].ToString());
            }
            return knownAssociations;
        }

        private static HttpResponseMessage SendWiqlToAzure(JObject query)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        Encoding.ASCII.GetBytes(
                            string.Format("{0}:{1}", "", PersonalAccessToken))));
                string url = $"{AzureHost}/{AzureProject}/_apis/wit/wiql?api-version=5.0";

                HttpMethod method = new HttpMethod("POST");

                var request = new HttpRequestMessage(method, url)
                {
                    Content = new StringContent(query.ToString(), Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response = new HttpResponseMessage();
                response = client.SendAsync(request).Result;

                response.EnsureSuccessStatusCode();
                return response;
            }
        }

        private static void PrintRunErrors(Dictionary<string, string> validAssociations, bool verbose = false)
        {
            string errors = string.Join("\n", AssociationErrors);
            Console.WriteLine($"Failing build due to the following errors: {errors}");
            Console.WriteLine($"Total valid test case associations: {validAssociations.Count}");
            Console.WriteLine($"Total errors: {AssociationErrors.Count}");
            if (verbose == true)
            {
                foreach (KeyValuePair<string, string> kvp in validAssociations)
                {
                    Console.WriteLine("Key = {0}, Value = {1}", kvp.Key, kvp.Value);
                }
            }
        }
        
        private static Dictionary<string, string> ProcessTestMethods(List<MethodInfo> testCases)
        {
            Dictionary<string, string> validAssociations = new Dictionary<string, string>();
            foreach (MethodInfo method in testCases)
            {
                bool assocationFound = false;
                object[] attrs = method.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    if (ContainsTestCaseId(attr))
                    {
                        assocationFound = true;
                        string methodPath = method.DeclaringType.FullName + "." + method.Name;
                        validAssociations[methodPath] = GetTestCaseId(attr);
                    }
                    else if (attr.Equals(attrs.Last()) && assocationFound == false)
                    {
                        AssociationErrors.Add($"MissingAssociationError: The following method is missing the [TestCaseId] attribute: {method.Name.ToString()}");
                    }
                }
            }

            return validAssociations;
        }

        private static string GetTestCaseId(object attr)
        {
            TestCaseIdAttribute testCaseIdAttribute = attr as TestCaseIdAttribute;
            return testCaseIdAttribute.TestCaseId;
        }


        private static bool ContainsTestCaseId(object attr)
        {
            bool containsTestCaseId = false;
            TestCaseIdAttribute testCaseIdAttribute = attr as TestCaseIdAttribute;
            if (testCaseIdAttribute != null)
            {
                containsTestCaseId = true;
            }

            return containsTestCaseId;

        }


        private static void UpdateAzureDevopsTestCase(string testCaseName, string testCaseId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json-patch+json"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(
                        Encoding.ASCII.GetBytes(
                            string.Format("{0}:{1}", "", PersonalAccessToken))));
                var method = new HttpMethod("PATCH");
                string newGuid = Guid.NewGuid().ToString();
                JArray operationsArray = (BuildAssociationParams(testCaseName, newGuid));
                string body = operationsArray.ToString();
                string url = $"{AzureHost}/DefaultCollection/_apis/wit/workitems/{testCaseId}?api-version=1.0";
                var request = new HttpRequestMessage(method, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json")
                };

                HttpResponseMessage response = new HttpResponseMessage();
                response = client.SendAsync(request).Result;
                try
                {
                    response.EnsureSuccessStatusCode();
                }
                catch (HttpRequestException)
                {
                    Console.WriteLine($"HTTP {response.StatusCode} has occurred while creating association for TestCaseId: {testCaseId}");
                    throw;
                }
            }
        }

        private static void AddTestCaseLinkToVSTSTest(Dictionary<string, string> TestMethods)
        {
            foreach (KeyValuePair<string, string> kvp in TestMethods)
            {
                bool testCaseNeedsChange = AutomatedFieldsNeedToChange(kvp.Key, kvp.Value);
                if (testCaseNeedsChange)
                {
                    UpdateAzureDevopsTestCase(kvp.Key, kvp.Value);
                    Console.WriteLine($"Updated test case ID {kvp.Value} to be associated with {kvp.Key}");
                }


            }
        }

        private static void DissociateTestCaseLinkage(string testCaseId)
        {
            using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json-patch+json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(
                        string.Format("{0}:{1}", "", PersonalAccessToken))));
            var method = new HttpMethod("PATCH");

            string body = DissociateParams().ToString();
            string url = $"{AzureHost}/DefaultCollection/_apis/wit/workitems/{testCaseId}?api-version=1.0";
            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json-patch+json")
            };

            HttpResponseMessage response = new HttpResponseMessage();
            response = client.SendAsync(request).Result;
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                Console.WriteLine($"HTTP {response.StatusCode} has occurred while creating association for TestCaseId: {testCaseId}");
                throw;
            }
        }
        }

        private static JArray BuildAssociationParams(string AutomatedTestName, string AutomatedTestId)
        {
            JArray operationsArray = new JArray
            {
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestName", AutomatedTestName),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestStorage", AutomatedTestDllName),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestId", AutomatedTestId),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestType", AutomatedTestType),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomationStatus", "Automated")
            };
            return operationsArray;
        }

        private static JArray DissociateParams()
        {
            JArray operationsArray = new JArray
            {
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestName", ""),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestStorage", ""),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestId", ""),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomatedTestType", ""),
                AddAddFieldToOperationsArray("/fields/Microsoft.VSTS.TCM.AutomationStatus", "")
            };
            return operationsArray;
        }

        private static JObject AddAddFieldToOperationsArray(string path, string value)
        {
            return new JObject()
            {
                {"op" ,"add"},
                {"path",  path},
                {"value", value}
            };
        }


    }
}
