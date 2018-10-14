using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Data;
using System.Configuration;
using System.Data.SqlClient;
using MySql.Data.MySqlClient;

namespace MyHome.Controllers
{
    public class HomeController : Controller
    {
        bool invalid = false;
        private MySqlConnection connection;

        public ActionResult Index()
        {
            var model = new User();

            ViewBag.Title = "Home Page";

            return View(model);
        }

        public async Task<ActionResult> GetInfo(User user)
        {
            if (string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.FirstName) || string.IsNullOrEmpty(user.LastName) || string.IsNullOrEmpty(user.Phone) || string.IsNullOrEmpty(user.Street) || string.IsNullOrEmpty(user.State) || string.IsNullOrEmpty(user.Country))
            {
                user.Error = "Please complete all the fields";
            }
            else
            {
                if (IsValidEmail(user.Email) && IsValidPhone(user.Phone))
                {
                    var baseUrl = "http://www.zillow.com/webservice/GetSearchResults.htm?zws-id=X1-ZWz18d3orsdurv_7zykg";
                    var streetAddress = user.Street.Replace(" ", "+").TrimEnd();
                    var encodedUrl = string.Format("{0}&address={1}&citystatezip={2}", baseUrl, streetAddress, user.City + "+" + user.State);
                    var requestUrl = encodedUrl + "&rentzestimate=true";
                    using (var http = new HttpClient())
                    {
                        //ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

                        var response = await http.GetAsync(requestUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            XmlDocument xmldoc = new XmlDocument();
                            var res = response.Content.ReadAsStringAsync().Result;
                            if (res.Contains("Error"))
                                user.Error = "Please check the input and try again";
                            else
                            {
                                xmldoc.LoadXml(res);
                                long homeZestimate = 0;
                                double rentZestimate = 0;
                                var xmlNodeString = xmldoc.ChildNodes[1].ChildNodes[2];
                                var fromXml = JsonConvert.SerializeXmlNode(xmlNodeString);
                                var replacedRate = Regex.Replace(fromXml, "(?<=\")[@#](?!.*\":\\s )", string.Empty, RegexOptions.IgnoreCase);
                                if (replacedRate.Contains("last-updated"))
                                {
                                    Regex.Replace(replacedRate, "last-updated", "last_updated", RegexOptions.IgnoreCase);
                                }
                                user.RentZestimate = JsonConvert.DeserializeObject<RootObject>(replacedRate);
                                if (user.RentZestimate.response.results.result.rentZestimate == null)
                                {
                                    homeZestimate = Convert.ToInt64(user.RentZestimate.response.results.result.zestimate.amount.text);
                                    rentZestimate = Math.Round((homeZestimate * 0.5) / 12);
                                    user.MonthlyRent = rentZestimate.ToString();
                                }
                                else
                                {
                                    user.MonthlyRent = user.RentZestimate.response.results.result.rentZestimate.amount.text;
                                }
                                user.IpAddress = GetIpAddress();
                                //SendEmail(user);
                                try
                                {
                                    SaveToDb(user);
                                }
                                catch (MySqlException e)
                                {
                                    Console.Write(e);
                                }
                            }
                        }
                    }
                }
                else
                {
                    user.Error = "Please provide valid Email/Phone.";
                }
            }
            return View("Index", user);
        }

        [Route("~/Home/SendEmail")]
        public void SendEmail(User user, string expectedrent)
        {
            var client = new SmtpClient();
            var body = new StringBuilder();
            body.AppendLine("Hi," + Environment.NewLine + "" + Environment.NewLine + "Thank you for using our service. Following is the detailed zestimate for your home." + Environment.NewLine + Environment.NewLine + "" + Environment.NewLine + "Your estimated monthly rent is: " + user.MonthlyRent + Environment.NewLine + "Address: " + user.Street + user.State + Environment.NewLine + "Zestimate: " + user.RentZestimate.response.results.result.zestimate.amount.text + Environment.NewLine + Environment.NewLine + "Thank you");

            var mailSubject = "Rent Zestimate";

            var mailFrom = "no_reply@myhomepro.info";
            var mailTo = user.Email;
            SendSmtpMessage(mailFrom, mailTo, mailSubject, body);

            Update(user, expectedrent);
        }

        public void SendSmtpMessage(string mailFrom, string mailTo, string mailSubject, StringBuilder body)
        {
            var client = new SmtpClient();
            try
            {
                var recipients = string.IsNullOrEmpty(mailTo) ? new string[0] : mailTo.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                var message = new MailMessage
                {
                    From = new MailAddress(mailFrom),
                    Body = body.ToString(),
                    Subject = mailSubject
                };
                foreach (var recipient in recipients)
                {
                    message.To.Add(new MailAddress(recipient));
                }
                client.Send(message);
            }
            catch (SmtpException e)
            {
                
            }
        }

        public bool IsValidEmail(string strIn)
        {
            invalid = false;
            if (String.IsNullOrEmpty(strIn))
                return false;

            // Use IdnMapping class to convert Unicode domain names.
            try
            {
                strIn = Regex.Replace(strIn, @"(@)(.+)$", this.DomainMapper,
                                      RegexOptions.None, TimeSpan.FromMilliseconds(200));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }

            if (invalid)
                return false;

            // Return true if strIn is in valid email format.
            try
            {
                return Regex.IsMatch(strIn,
                      @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                      @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-0-9a-z]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$",
                      RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private string DomainMapper(Match match)
        {
            // IdnMapping class with default property values.
            IdnMapping idn = new IdnMapping();

            string domainName = match.Groups[2].Value;
            try
            {
                domainName = idn.GetAscii(domainName);
            }
            catch (ArgumentException)
            {
                invalid = true;
            }
            return match.Groups[1].Value + domainName;
        }

        public bool IsValidPhone(string strIn)
        {
            if (String.IsNullOrEmpty(strIn))
                return false;
            if (strIn.Length > 10)
                return false;

            try
            {
                return Regex.IsMatch(strIn, @"\(?\d{3}\)?-? *\d{3}-? *-?\d{4}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        protected string GetIpAddress()
        {
            var request = System.Web.HttpContext.Current.Request;
            if (request == null) return string.Empty;

            var headers = request.Headers;

            var forwardedFor = headers.Get("X-Forwarded-For");
            if (string.IsNullOrEmpty(forwardedFor))
                forwardedFor = request.ServerVariables["HTTP_X_FORWARDED_FOR"];
            if (string.IsNullOrEmpty(forwardedFor))
                forwardedFor = request.ServerVariables["REMOTE_ADDR"];
            if (forwardedFor == "::1")
                return "166.76.0.1";

            if (forwardedFor != null && forwardedFor.Contains(","))
            {
                var ips = forwardedFor.Split(',');
                forwardedFor = ips.FirstOrDefault(ip => ip != null && !string.IsNullOrWhiteSpace(ip) && !ip.StartsWith("10."));
            }

            return forwardedFor;
        }

        public void SaveToDb(User user)
        {
            try
            {
                connection = new MySqlConnection();

                connection.ConnectionString = ConfigurationManager.ConnectionStrings["Default"].ConnectionString;

                connection.Open();
                try
                {
                    MySqlCommand cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandText = string.Format("INSERT INTO `MyHomeProMySql`.`user` (`firstname`,`lastname`,`email`,`phone`,`street`,`city`,`state`,`country`,`zipcode`,`error`,`expectedrent`,`rentzestimate`,`propertyid`,`ipaddress`) VALUES (@FirstName, @LastName, @Email, @Phone, @Street, @City, @State, @Country, @ZipCode, @Error, @ExpectedRent, @RentZestimate, @PropertyId, @IpAddress)");
                    cmd.Parameters.AddWithValue("@FirstName", user.FirstName);
                    cmd.Parameters.AddWithValue("@LastName", user.LastName);
                    cmd.Parameters.AddWithValue("@Email", user.Email);
                    cmd.Parameters.AddWithValue("@Phone", user.Phone);
                    cmd.Parameters.AddWithValue("@Street", user.Street);
                    cmd.Parameters.AddWithValue("@City", user.City);
                    cmd.Parameters.AddWithValue("@State", user.State);
                    cmd.Parameters.AddWithValue("@Country", user.Country);
                    cmd.Parameters.AddWithValue("@ZipCode", user.ZipCode);
                    cmd.Parameters.AddWithValue("@Error", user.Error);
                    cmd.Parameters.AddWithValue("@ExpectedRent", user.ExpectedRent);
                    cmd.Parameters.AddWithValue("@RentZestimate", user.MonthlyRent);
                    cmd.Parameters.AddWithValue("@PropertyId", user.RentZestimate.response.results.result.zpid);
                    cmd.Parameters.AddWithValue("@IpAddress", user.IpAddress);
                    MySqlDataReader reader = cmd.ExecuteReader();

                    reader.Read();

                    reader.Close();
                }
                catch (MySqlException e)
                {
                    Console.Write(e);
                }
            }
            catch (MySqlException e)
            {
                Console.Write(e);
            }

        }

        public void Update(User user, string expectedrent)
        {
            connection = new MySqlConnection();

            connection.ConnectionString = ConfigurationManager.ConnectionStrings["Default"].ConnectionString;

            connection.Open();
            try
            {
                MySqlCommand cmd = new MySqlCommand();
                cmd.Connection = connection;
                cmd.CommandText = string.Format("UPDATE `MyHomeProMySql`.`user` SET user.expectedrent = @ExpectedRent WHERE user.email = '{0}'",user.Email);
                cmd.Parameters.AddWithValue("@ExpectedRent", expectedrent);
                MySqlDataReader reader = cmd.ExecuteReader();

                reader.Read();

                reader.Close();
            }
            catch (MySqlException e)
            {
                Console.Write(e);
            }
        }
    }

    public class User
    {
        [Display(Name = "FirstName")]
        public string FirstName { get; set; }

        [Display(Name = "LastName")]
        public string LastName { get; set; }

        [Display(Name = "Email")]
        public string Email { get; set; }

        [Display(Name = "Phone")]
        public string Phone { get; set; }

        [Display(Name = "Address")]
        public string Street { get; set; }

        [Display(Name = "City")]
        public string City { get; set; }

        [Display(Name = "State")]
        public string State { get; set; }

        [Display(Name = "Country")]
        public string Country { get; set; }

        [Display(Name = "ZipCode")]
        public string ZipCode { get; set; }

        public string Error { get; set; }

        public RootObject RentZestimate { get; set; }

        public string MonthlyRent { get; set; }

        public string ExpectedRent { get; set; }

        public string IpAddress { get; set; }
    }

    public class Links
    {
        public string homedetails { get; set; }
        public string graphsanddata { get; set; }
        public string mapthishome { get; set; }
        public string comparables { get; set; }
    }

    public class Address
    {
        public string street { get; set; }
        public string zipcode { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string latitude { get; set; }
        public string longitude { get; set; }
    }

    public class Amount
    {
        public string currency { get; set; }
        public string text { get; set; }
    }

    public class OneWeekChange
    {
        public string deprecated { get; set; }
    }

    public class ValueChange
    {
        public string duration { get; set; }
        public string currency { get; set; }
        public string text { get; set; }
    }

    public class Low
    {
        public string currency { get; set; }
        public string text { get; set; }
    }

    public class High
    {
        public string currency { get; set; }
        public string text { get; set; }
    }

    public class ValuationRange
    {
        public Low low { get; set; }
        public High high { get; set; }
    }

    public class Zestimate
    {
        public Amount amount { get; set; }
        public string last_updated { get; set; }
        public OneWeekChange oneWeekChange { get; set; }
        public ValueChange valueChange { get; set; }
        public ValuationRange valuationRange { get; set; }
        public string percentile { get; set; }
    }

    public class RentZestimate
    {
        public Amount amount { get; set; }
        public string last_updated { get; set; }
        public OneWeekChange oneWeekChange { get; set; }
        public ValueChange valueChange { get; set; }
        public ValuationRange valuationRange { get; set; }
    }

    public class Links2
    {
        public string overview { get; set; }
        public string forSaleByOwner { get; set; }
        public string forSale { get; set; }
    }

    public class Region
    {
        public string name { get; set; }
        public string id { get; set; }
        public string type { get; set; }
        public string zindexValue { get; set; }
        public Links2 links { get; set; }
    }

    public class LocalRealEstate
    {
        public Region region { get; set; }
    }

    public class Result
    {
        public string zpid { get; set; }
        public Links links { get; set; }
        public Address address { get; set; }
        public Zestimate zestimate { get; set; }
        public LocalRealEstate localRealEstate { get; set; }
        public RentZestimate rentZestimate { get; set; }
    }

    public class Results
    {
        public Result result { get; set; }
    }

    public class Response
    {
        public Results results { get; set; }
    }

    public class RootObject
    {
        public Response response { get; set; }
    }

}
