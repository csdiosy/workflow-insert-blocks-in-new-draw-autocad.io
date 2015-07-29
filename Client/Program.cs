using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Dynamic;

namespace Client
{
    class Credentials
    {
        //get your ConsumerKey/ConsumerSecret at http://developer.autodesk.com
        public static string ConsumerKey = "";
        public static string ConsumerSecret = "";
    }
    class Program
    {

        static readonly string PackageName = "MyTestPackage";
        static readonly string ActivityName = "MyTestActivity";

        static AIO.Container container;
        static void Main(string[] args)
        {
            //instruct client side library to insert token as Authorization value into each request
            container = new AIO.Container(new Uri("https://developer.api.autodesk.com/autocad.io/v1/"));
            var token = GetToken();
            container.SendingRequest2 += (sender, e) => e.RequestMessage.SetHeader("Authorization", token);

            //check if our app package exists
            var package = container.AppPackages.Where(a => a.Id == PackageName).FirstOrDefault();
            string res = null;
            if (package!=null)
                res = Prompts.PromptForKeyword(string.Format("AppPackage '{0}' already exists. What do you want to do? [Recreate/Update/Leave]<Update>", PackageName));
            if (res == "Recreate")
            {
                container.DeleteObject(package);
                container.SaveChanges();
                package = null;
            }       
            if (res!="Leave")
                package = CreateOrUpdatePackage(CreateZip(), package);

            //check if our activity already exist
            var activity = container.Activities.Where(a => a.Id == ActivityName).FirstOrDefault();
            if (activity != null)
            {
                if (Prompts.PromptForKeyword(string.Format("Activity '{0}' already exists. Do you want to recreate it? [Yes/No]<No>", ActivityName)) == "Yes")
                {
                    container.DeleteObject(activity);
                    container.SaveChanges();
                    activity  = null;
                }
            }
            if (activity == null)
                activity = CreateActivity(package);

            //save outstanding changes if any
            container.SaveChanges(System.Data.Services.Client.SaveChangesOptions.PatchOnUpdate);

            //finally submit workitem against our activity
            //file with blocks using inline json syntax
            Console.WriteLine("RemoteFileResource");
            dynamic files = new ExpandoObject();
            files.Resource = "https://s3.amazonaws.com/" +
                    "AutoCAD-Core-Engine-Services/TestDwg/acad.dwt";
            files.LocalFileName = "Prazan.dwg";

            files.RelatedFiles = new ExpandoObject[6];
            files.RelatedFiles[0] = new ExpandoObject();
            files.RelatedFiles[0].Resource = "http://csaba.somee.com/bokl.dwg";
            files.RelatedFiles[0].LocalFileName = "bokl.dwg";


            files.RelatedFiles[1] = new ExpandoObject();
            files.RelatedFiles[1].Resource = "http://csaba.somee.com/bd24pnt.dwg";
            files.RelatedFiles[1].LocalFileName = "bd24pnt.dwg";

            files.RelatedFiles[2] = new ExpandoObject();
            files.RelatedFiles[2].Resource = "http://csaba.somee.com/formpro.dwg";
            files.RelatedFiles[2].LocalFileName = "formpro.dwg";

            files.RelatedFiles[3] = new ExpandoObject();
            files.RelatedFiles[3].Resource = "http://csaba.somee.com/korak700.dwg";
            files.RelatedFiles[3].LocalFileName = "korak700.dwg";

            files.RelatedFiles[4] = new ExpandoObject();
            files.RelatedFiles[4].Resource = "http://csaba.somee.com/bd24pnvz.dwg";
            files.RelatedFiles[4].LocalFileName = "bd24pnvz.dwg";

            files.RelatedFiles[5] = new ExpandoObject();
            files.RelatedFiles[5].Resource = "http://csaba.somee.com/bokdl.dwg";
            files.RelatedFiles[5].LocalFileName = "bokdl.dwg";

            var json = JsonConvert.SerializeObject(files);
            SubmitWorkItem(activity, json, "RemoteFileResource");


        }

        static string GetToken()
        {
            Console.WriteLine("Getting authorization token...");
            using (var client = new HttpClient())
            {
                var values = new List<KeyValuePair<string, string>>();
                values.Add(new KeyValuePair<string, string>("client_id", Credentials.ConsumerKey));
                values.Add(new KeyValuePair<string, string>("client_secret", Credentials.ConsumerSecret));
                values.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
                var requestContent = new FormUrlEncodedContent(values);
                var response = client.PostAsync("https://developer.api.autodesk.com/authentication/v1/authenticate", requestContent).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                var resValues = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                return resValues["token_type"] + " " + resValues["access_token"];
            }
        }
        static string CreateZip()
        {
            Console.WriteLine("Generating autoloader zip...");
            string zip = "package.zip";
            if (File.Exists(zip))
                File.Delete(zip);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                string bundle = PackageName + ".bundle";
                string name = "PackageContents.xml";
                archive.CreateEntryFromFile(name, Path.Combine(bundle, name));
                name = "Newtonsoft.Json.dll";
                archive.CreateEntryFromFile(name, Path.Combine(bundle, "Contents", name));
            }
            return zip;

        }

        static AIO.AppPackage CreateOrUpdatePackage(string zip, AIO.AppPackage package)
        {
            Console.WriteLine("Creating/Updating AppPackage...");
            // First step -- query for the url to upload the AppPackage file
            UriBuilder builder = new UriBuilder(container.BaseUri);
            builder.Path += "AppPackages/GenerateUploadUrl";
            var url = container.Execute<string>(builder.Uri, "POST", true, null).First();

            // Second step -- upload AppPackage file
            UploadObject(url, zip);

            if (package == null)
            {
                // third step -- after upload, create the AppPackage entity
                package = new AIO.AppPackage()
                {
                    UserId = "",
                    Id = PackageName,
                    Version = 1,
                    RequiredEngineVersion = "20.0",
                    Resource = url
                };
                container.AddToAppPackages(package);
            }
            else
            {
                //or update the existing one with the new url
                package.Resource = url;
                container.UpdateObject(package);
            }
            container.SaveChanges(System.Data.Services.Client.SaveChangesOptions.PatchOnUpdate);
            return package;
        }

        static void UploadObject(string url, string filePath)
        {
            Console.WriteLine("Uploading autoloader zip...");
            var client = new HttpClient();
            client.PutAsync(url, new StreamContent(File.OpenRead(filePath))).Result.EnsureSuccessStatusCode();
        }


        //creates an activity with inputs and variable number of outputs. 
        static AIO.Activity CreateActivity(AIO.AppPackage package)
        {
            Console.WriteLine("Creating/Updating Activity...");
            var activity = new AIO.Activity()
            {
                UserId = "",
                Id = ActivityName,
                Version = 1,
                Instruction = new AIO.Instruction()
                {
                    Script =
                      "_tilemode 1 "+
                      "GRID OFF " +
                      "insert formpro 0,0   \n" +
                      "zoom e\n" +
                      "insert bokl 108.38,34.885 0.666  0\n" +
                      "insert korak700 108.38,34.885 0.666  0\n" +
                      "insert BD24pnt 108.38,34.885 0.666  0\n" +
                      "insert korak700 155,34.885 0.666  0\n" +
                      "insert BD24pnvz 155,34.885 0.666  0\n" +
                      "insert bokdl 201.62,34.885 0.666  0\n" +
                      "LTSCALE 13\n" +
                      "_tilemode 0 " +
                      "_save result.dwg\n" +
                      "_-export _pdf _all result.pdf\n"
                },
                Parameters = new AIO.Parameters()
                {
                    InputParameters = {
                        new AIO.Parameter() { Name = "HostDwg", LocalFileName = "$(HostDwg)" },
                    },
                    OutputParameters = {
                        new AIO.Parameter() { Name = "PdfResult", LocalFileName = "result.pdf" },
                    }
                },
                RequiredEngineVersion = "20.0"
            };
            container.AddToActivities(activity);
            container.SaveChanges(System.Data.Services.Client.SaveChangesOptions.PatchOnUpdate);
            //establish link to package
            container.AddLink(activity, "AppPackages", package);
            container.SaveChanges();
            return activity;
        }

        static void SubmitWorkItem(AIO.Activity activity, string inResource, string inResourceKind)
        {
            Console.WriteLine("Submitting workitem...");
            //create a workitem
            var wi = new AIO.WorkItem()
            {
                UserId = "", //must be set to empty
                Id = "", //must be set to empty
                Arguments = new AIO.Arguments(),
                Version = 1, //should always be 1
                ActivityId = new AIO.EntityId { Id= activity.Id, UserId = activity.UserId }
            };

            wi.Arguments.InputArguments.Add(new AIO.Argument()
            {
                Name = "HostDwg",// Must match the input parameter in activity
                Resource = inResource,
                ResourceKind = inResourceKind,
                StorageProvider = "Generic" //Generic HTTP download (as opposed to A360)
            });

            wi.Arguments.OutputArguments.Add(new AIO.Argument()
          {
              Name = "PdfResult", // Must match activity's output param
              StorageProvider = "Generic", // Generic HTTP upload
              HttpVerb = "POST", // Use HTTP POST when delivering result
              Resource = null // Use storage provided by AutoCAD.io
          }
);

            container.AddToWorkItems(wi);
            container.SaveChanges();

            //polling loop
            do
            {
                Console.WriteLine("Sleeping for 5 sec...");
                System.Threading.Thread.Sleep(5000);
                container.LoadProperty(wi, "Status"); //http request is made here
                Console.WriteLine("WorkItem status: {0}", wi.Status);
            }
            while (wi.Status == "Pending" || wi.Status == "InProgress");

            //re-query the service so that we can look at the details provided by the service
            container.MergeOption = System.Data.Services.Client.MergeOption.OverwriteChanges;
            wi = container.WorkItems.Where(p => p.UserId == wi.UserId && p.Id == wi.Id).First();

            //Resource property of the output argument "Results" will have the output url
            var url = wi.Arguments.OutputArguments.First(a => a.Name == "PdfResult").Resource;
            DownloadToDocs(url, "AIO.pdf");

            //download the status report
            url = wi.StatusDetails.Report;
            DownloadToDocs(url, "AIO-report.txt");
        }

        static void DownloadToDocs(string url, string localFile)
        {
            var client = new HttpClient();
            var content = (StreamContent)client.GetAsync(url).Result.Content;
            var fname = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), localFile);
            Console.WriteLine("Downloading to {0}.", fname);
            using (var output = System.IO.File.Create(fname))
            {
                content.ReadAsStreamAsync().Result.CopyTo(output);
                output.Close();
            }
        }
    }
}
