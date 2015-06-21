﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Script.Services;
using System.Web.Security;
using System.Web.Services;
using Cognifide.PowerShell.Core.Host;
using Cognifide.PowerShell.Core.Settings;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Exceptions;
using Sitecore.Jobs;
using Sitecore.Security.Accounts;
using LicenseManager = Sitecore.SecurityModel.License.LicenseManager;

namespace Cognifide.PowerShell.Console.Services
{
    /// <summary>
    ///     Summary description for PowerShellWebService
    ///     The service is used by Terminal/Console app and the Sitecore Rocks Visual Studio console
    ///     for all of their operations. It's also used by ISE for code completions.
    /// </summary>
    [WebService(Namespace = "http://sitecorepowershellextensions/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    [ScriptService]
    public class PowerShellWebService : WebService
    {
        public const string StatusComplete = "complete";
        public const string StatusPartial = "partial";
        public const string StatusWorking = "working";
        public const string StatusError = "error";

        [WebMethod(EnableSession = true)]
        public void LoginUser(string userName, string password)
        {
            if (!WebServiceSettings.ServiceEnabledClient)
            {
                return;
            }

            if (!userName.Contains("\\"))
            {
                userName = "sitecore\\" + userName;
            }
            if (Sitecore.Context.IsLoggedIn)
            {
                if (Sitecore.Context.User.Name.Equals(userName, StringComparison.OrdinalIgnoreCase))
                    return;
                Sitecore.Context.Logout();
            }
            if (!LicenseManager.HasContentManager && !LicenseManager.HasExpress)
                throw new AccessDeniedException("A required license is missing");
            Assert.IsTrue(Membership.ValidateUser(userName, password), "Unknown username or password.");
            var user = Sitecore.Security.Accounts.User.FromName(userName, true);
            UserSwitcher.Enter(user);
        }

        [WebMethod(EnableSession = true)]
        public object ExecuteRocksCommand(string guid, string command, string username, string password)
        {
            if (!WebServiceSettings.ServiceEnabledClient)
            {
                return string.Empty;
            }
            LoginUser(username, password);
            if (!Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            return ExecuteCommand(guid, command, "text");
        }

        [WebMethod(EnableSession = true)]
        public object ExecuteCommand(string guid, string command, string stringFormat)
        {
            if (!WebServiceSettings.ServiceEnabledClient)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();
            var output = new StringBuilder();

            if (!HttpContext.Current.Request.IsAuthenticated &&
                !command.StartsWith("login-user", StringComparison.OrdinalIgnoreCase))
            {
                return serializer.Serialize(
                    new
                    {
                        result =
                            "You need to be authenticated to use the PowerShell console. Please login to Sitecore first.",
                        prompt = "PS >"
                    });
            }

            var session = GetScriptSession(guid);
            try
            {
                var handle = ID.NewID.ToString();
                var jobOptions = new JobOptions(GetJobId(guid, handle), "PowerShell", "shell", this, "RunJob",
                    new object[] {session, command})
                {
                    AfterLife = new TimeSpan(0, 10, 0),
                    ContextUser = Sitecore.Context.User,
                    EnableSecurity = true,
                    ClientLanguage = Sitecore.Context.ContentLanguage
                };
                JobManager.Start(jobOptions);
                Thread.Sleep(WebServiceSettings.CommandWaitMillis);
                return PollCommandOutput(guid, handle, stringFormat);
            }
            catch (Exception ex)
            {
                return
                    serializer.Serialize(
                        new Result
                        {
                            status = StatusError,
                            result = output +
                                     "\r\n[[;#f00;#000]Ooops, something went wrong... Do you need assistance?]\r\n" +
                                     "[[;#f00;#000]Send an email with the stack trace to adam@najmanowicz.com or contact me on Twitter @AdamNaj]\r\n\r\n" +
                                     session.GetExceptionConsoleString(ex) + "\r\n",
                            prompt = string.Format("PS {0}>", session.CurrentLocation)
                        });
            }
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object KeepAlive(string guid)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var sessionExists = ScriptSessionManager.SessionExists(guid);
            return sessionExists ? "alive" : "session-not-found";
        }

        private static ScriptSession GetScriptSession(string guid)
        {
            return ScriptSessionManager.GetSession(guid, ApplicationNames.AjaxConsole, false);
        }

        [WebMethod(EnableSession = true)]
        protected void RunJob(ScriptSession session, string command)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return;
            }
            try
            {
                session.ExecuteScriptPart(command);
            }
            catch (Exception ex)
            {
                var job = Sitecore.Context.Job;
                if (job != null)
                {
                    job.Status.Failed = true;
                    job.Status.Messages.Add("Ooops, something went wrong... Do you need assistance?");
                    job.Status.Messages.Add(
                        "Send an email with the stack trace to adam@najmanowicz.com or contact me on Twitter @AdamNaj");
                    job.Status.LogException(ex);
                }
                else
                {
                    Log.Error("Script execution failed. Could not find command job.", ex, this);
                }
            }
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object PollCommandOutput(string guid, string handle, string stringFormat)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();

            var session = GetScriptSession(guid);
            var result = new Result();
            var scriptJob = JobManager.GetJob(GetJobId(guid, handle));
            if (scriptJob == null)
            {
                result.status = StatusError;
                result.result =
                    "Can't find your command result. This might mean that your session has timed out or your script caused the application to restart.";
                result.prompt = string.Format("PS {0}>", session.CurrentLocation);
                session.Output.Clear();
                return serializer.Serialize(result);
            }

            result.handle = handle;
            if (!scriptJob.IsDone)
            {
                result.status = StatusWorking;
                return serializer.Serialize(result);
            }
            if (scriptJob.Status.Failed)
            {
                result.status = StatusError;
                var message =
                    string.Join(Environment.NewLine, scriptJob.Status.Messages.Cast<string>().ToArray())
                        .Replace("[", "&#91;")
                        .Replace("]", "&#93;");
                result.result = "[[;#f00;#000]" + (message.Length > 0 ? message : "Command failed") + "]";
                result.prompt = string.Format("PS {0}>", session.CurrentLocation);
                session.Output.Clear();
                return serializer.Serialize(result);
            }
            result.status = StatusComplete;
            var lines = 0;
            var buffer = WebServiceSettings.SerializationSizeBuffer;
            var partial = false;
            var temp = new StringBuilder();
            var output = new StringBuilder();
            foreach (var outputLine in session.Output)
            {
                outputLine.GetLine(temp, stringFormat);
                if ((output.Length + temp.Length + buffer) > 131072)
                {
                    session.Output.RemoveRange(0, lines);
                    partial = true;
                    break;
                }
                lines++;
                output.Append(temp);
                temp.Remove(0, temp.Length);
            }
            result.result = output.ToString().TrimEnd('\r', '\n');
            result.prompt = string.Format("PS {0}>", session.CurrentLocation);
            if (partial)
            {
                result.status = StatusPartial;
            }
            else
            {
                session.Output.Clear();
            }
            HttpContext.Current.Response.ContentType = "application/json";
            var serializedResult = serializer.Serialize(result);
            return serializedResult;
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public string[] CompleteRocksCommand(string guid, string command, string username, string password)
        {
            if (!WebServiceSettings.ServiceEnabledClient)
            {
                return new string[0];
            }
            LoginUser(username, password);
            if (!Sitecore.Context.IsLoggedIn)
            {
                return new string[0];
            }
            return GetTabCompletionOutputs(guid, command, false);
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object CompleteAceCommand(string guid, string command)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();
            var result = serializer.Serialize(GetTabCompletionOutputs(guid, command, true));
            return result;
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object CompleteCommand(string guid, string command)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();
            var result = serializer.Serialize(GetTabCompletionOutputs(guid, command, false));
            return result;
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object GetAutoCompletionPrefix(string guid, string command)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();
            var session = GetScriptSession(guid);
            try
            {
                var result = serializer.Serialize(CommandCompletion.GetPrefix(session, command));
                return result;
            }
            finally
            {
                if (string.IsNullOrEmpty(guid))
                {
                    ScriptSessionManager.RemoveSession(session);
                }
            }
        }

        public static string[] GetTabCompletionOutputs(string guid, string command, bool lastTokenOnly)
        {
            var session = GetScriptSession(guid);
            try
            {
                var result = CommandCompletion.FindMatches(session, command, lastTokenOnly);
                return result.ToArray();
            }
            finally
            {
                if (string.IsNullOrEmpty(guid))
                {
                    ScriptSessionManager.RemoveSession(session);
                }
            }
        }

        [WebMethod(EnableSession = true)]
        [ScriptMethod(UseHttpGet = false, ResponseFormat = ResponseFormat.Json)]
        public object GetHelpForCommand(string guid, string command)
        {
            if (!WebServiceSettings.ServiceEnabledClient || !Sitecore.Context.IsLoggedIn)
            {
                return string.Empty;
            }
            var serializer = new JavaScriptSerializer();
            var result = serializer.Serialize(GetHelpOutputs(guid, command));
            return result;
        }

        public static string[] GetHelpOutputs(string guid, string command)
        {
            var session = GetScriptSession(guid);
            try
            {
                var result = CommandHelp.GetHelp(session, command);
                return result.ToArray();
            }
            finally
            {
                if (string.IsNullOrEmpty(guid))
                {
                    ScriptSessionManager.RemoveSession(session);
                }
            }
        }

        public static string GetJobId(string sessionGuid, string handle)
        {
            return "PowerShell-" + sessionGuid + "-" + handle;
        }

        // TODO: Using the default JavaScript Serializer prevents us from being able to use PascalCasing.
        public class Result
        {
            public string handle;
            public string prompt;
            public string result;
            public string status;
        }
    }
}