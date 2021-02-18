using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using RestSharp;

namespace BTC_Prices_Widget
{
    class Program
    {
        static string connectionString = "Server=tcp:btcwidgetserver.database.windows.net,1433;Initial Catalog=btcwidgetdb;Persist Security Info=False;User ID=ekatwood;Password=ek@132EKA;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        
        //how many minutes to look back when filling the data structure
        const int minutesLookBack = 1440;

        //Always make sure Coins and coinsListOfficial are synced up.
        enum Coins
        {
            ONEINCH, AAVE, ADA, ALGO, ATOM, AVAX, BAT, BCH, BSV, BTC, BTT, CEL, CHSB, COMP, CRV, DASH, DCR, DGB, DOGE, DOT, EGLD, EOS, ETC, ETH, FIL, FTT, GRT, HBAR, HT, KSM, LINK, LRC, LTC, LUNA, MIOTA, MKR, NANO, NEAR, NEO, OMG, ONT, QNT, REN, RENBTC, RUNE, SC, SNX, SOL, SUSHI, THETA, TRX, UMA, UNI, VET, VGX, WAVES, WBTC, XEM, XLM, XMR, XRP, XTZ, YFI, ZEC, ZEN, ZIL, ZRX
        }

        const string coinsListOfficial = "ONEINCH,AAVE,ADA,ALGO,ATOM,AVAX,BAT,BCH,BSV,BTC,BTT,CEL,CHSB,COMP,CRV,DASH,DCR,DGB,DOGE,DOT,EGLD,EOS,ETC,ETH,FIL,FTT,GRT,HBAR,HT,KSM,LINK,LRC,LTC,LUNA,MIOTA,MKR,NANO,NEAR,NEO,OMG,ONT,QNT,REN,RENBTC,RUNE,SC,SNX,SOL,SUSHI,THETA,TRX,UMA,UNI,VET,VGX,WAVES,WBTC,XEM,XLM,XMR,XRP,XTZ,YFI,ZEC,ZEN,ZIL,ZRX";

        static void Main()
        {
            //return;
            //set up variable
            List<string> users = new List<string> { };

            //load the datastructure of coin prices
            double[,] prices = getPrices(coinsListOfficial);

            //connect to db
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    //get all users with jobs to do
                    using (var command = new SqlCommand("GetJobs", connection))
                    {

                        command.CommandType = CommandType.StoredProcedure;

                        SqlDataReader r = command.ExecuteReader();

                        while (r.Read())
                        {
                            Console.WriteLine("Adding user " + (string)r["UserName"]);
                            users.Add((string)r["UserName"]);
                        }

                        r.Close();
                    }

                    //cycle through all users who have jobs
                    foreach (string user in users)
                    {
                        Console.WriteLine("Starting jobs for user " + user);

                        string emailSubject = "";
                        string emailBody = "\n";
                        bool sendEmail = false;

                        //variables to hold the jobs - (List of coins, percent change, minutes)
                        var jobs = new List<(string, int, int)> { };

                        //get the jobs
                        using (var command = new SqlCommand("GetJobsForUser", connection))
                        {

                            command.CommandType = CommandType.StoredProcedure;

                            command.Parameters.Add("@user", SqlDbType.VarChar).Value = user;

                            SqlDataReader r = command.ExecuteReader();


                            while (r.Read())
                            {
                                Console.WriteLine("Adding job: (" + (string)r["ListOfCoins"] + "," + Convert.ToString((int)r["PercentChange"]) + "," + Convert.ToString((int)r["Minutes"]) + ")");
                                jobs.Add(((string)r["ListOfCoins"], (int)r["PercentChange"], (int)r["Minutes"]));
                            }

                            r.Close();
                        }

                        

                        //do the jobs
                        foreach (var job in jobs)
                        {
                            Console.WriteLine("Starting job:");
                            Console.WriteLine(job.Item1);
                            Console.WriteLine(job.Item2);
                            Console.WriteLine(job.Item3);

                            //split the coins they are watching into a list
                            string[] coins = job.Item1.Replace("\"", "").Split(',');


                            foreach (string c in coins)
                            {
                                Console.WriteLine("Current coin: " + c);

                                double percentChange = getPercent(prices, job.Item3, c);

                                Console.WriteLine("Percent change: " + percentChange);

                                bool coinFound = false;

                                if ((percentChange > 0 && percentChange >= job.Item2 && job.Item2 > 0) || (percentChange < 0 && percentChange <= job.Item2 && job.Item2 < 0))
                                {
                                    //check if it already sent this hour
                                    TimeSpan t = DateTime.UtcNow - new DateTime(1970, 1, 1);
                                    long now = (long)t.TotalSeconds;

                                    int oneHour = 3600;
                                    long lastNotificationTime = 0;

                                    
                                    //get last notification time
                                    using (var command = new SqlCommand("GetNotifiedTime", connection))
                                    {

                                        command.CommandType = CommandType.StoredProcedure;

                                        command.Parameters.Add("@user", SqlDbType.VarChar).Value = user;
                                        command.Parameters.Add("@coin", SqlDbType.VarChar).Value = c;

                                        SqlDataReader r = command.ExecuteReader();


                                        while (r.Read())
                                        {
                                            lastNotificationTime = (long)r["NotifiedTime"];
                                            coinFound = true;
                                        }

                                        r.Close();
                                    }

                                    //dont send more than 1 notification an hour
                                    if (now > (lastNotificationTime + oneHour) && coinFound)
                                    {
                                        sendEmail = true;

                                        //set up email message
                                        int minutes = job.Item3;

                                        if(emailSubject == "")
                                        {
                                            emailSubject = c;
                                        }
                                        else
                                        {
                                            emailSubject = emailSubject + "," + c;
                                        }
                                        
                                        if(percentChange < 0)
                                        {
                                            emailBody = emailBody + c + " fell by " + Math.Round(percentChange, 2) + "% in ";
                                        }
                                        else
                                        {
                                            emailBody = emailBody + c + " rose by " + Math.Round(percentChange, 2) + "% in ";
                                        }

                                        

                                        if (minutes > 55)
                                        {
                                            //convert minutes into hours for formatting
                                            if (minutes == 60)
                                            {
                                                emailBody = emailBody + "1 hour.\n\n";
                                            }
                                            else
                                            {
                                                emailBody = emailBody + (minutes / 60).ToString() + " hours.\n\n";
                                            }
                                        }
                                        else
                                        {
                                            emailBody = emailBody + minutes.ToString() + " minutes.\n\n";
                                        }

                                        //update notified time of coin
                                        using (var command = new SqlCommand("UpdateNotifiedTime", connection))
                                        {

                                            command.CommandType = CommandType.StoredProcedure;

                                            command.Parameters.Add("@user", SqlDbType.VarChar).Value = user;
                                            command.Parameters.Add("@time", SqlDbType.BigInt).Value = now;
                                            command.Parameters.Add("@coin", SqlDbType.VarChar).Value = c;

                                            command.ExecuteNonQuery();

                                        }
                                    }
                                }

                            }
                        }

                        if (sendEmail)
                        {
                            //set up email subject
                            if (emailSubject.Contains(","))
                            {
                                var coinses = emailSubject.Split(',');

                                //set up subject
                                if(emailBody.Contains("fell") && emailBody.Contains("rose"))
                                {
                                    emailSubject = coinses[0] + " and more are changing fast! " + Char.ConvertFromUtf32(0xE10D);
                                }
                                else if (emailBody.Contains("fell"))
                                {
                                    emailSubject = coinses[0] + " and more are falling fast! " + Char.ConvertFromUtf32(0x1f4c9);
                                }
                                else if (emailBody.Contains("rose"))
                                {
                                    emailSubject = coinses[0] + " and more are rising fast! " + Char.ConvertFromUtf32(0xE10D);
                                }

                                
                            }
                            else
                            {
                                //set up subject
                                if (emailBody.Contains("fell"))
                                {
                                    emailSubject = emailSubject + " is falling fast! " + Char.ConvertFromUtf32(0x1f4c9);
                                }
                                else
                                {
                                    emailSubject = emailSubject + " is rising fast! " + Char.ConvertFromUtf32(0xE10D);
                                }
                                
                            }

                            //set up mail client
                            var smtpClient = new SmtpClient("smtp.gmail.com");

                            smtpClient.UseDefaultCredentials = false;
                            smtpClient.Port = 587;
                            smtpClient.Credentials = new NetworkCredential("eric.atwood12@gmail.com", "Papspd9@@");
                            smtpClient.EnableSsl = true;

                            //send email
                            smtpClient.Send("eric.atwood12@gmail.com", user, emailSubject, emailBody);

                        }
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine("An exception occured.");

                    //log exception
                    using (var command = new SqlCommand("LogException", connection))
                    {

                        command.CommandType = CommandType.StoredProcedure;

                        command.Parameters.Add("@type", SqlDbType.VarChar).Value = e.GetType();
                        command.Parameters.Add("@message", SqlDbType.BigInt).Value = e.Message;

                        command.ExecuteNonQuery();

                    }

                    //set up mail client
                    var smtpClient = new SmtpClient("smtp.gmail.com");

                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.Port = 587;
                    smtpClient.Credentials = new NetworkCredential("eric.atwood12@gmail.com", "Papspd9@@");
                    smtpClient.EnableSsl = true;

                    //send email
                    smtpClient.Send("eric.atwood12@gmail.com", "eric.atwood12@gmail.com", "Error in WebJob", e.Message);

                    throw e;
                }

                connection.Close();
            }
        }

        static double[,] getPrices(string coins)
        {
            //takes one extra than the size you need
            double[,] prices = new double[coins.Length, minutesLookBack + 1];

            //counter for x var in [,]
            //int i = 0;

            foreach (string c in coins.Split(','))
            {
                var api = "fa2ca887d1dca3a2f8bd320400b4fa8eb9006d6d4190a25a003283280a1a8cf4";

                var restClient = new RestClient("https://min-api.cryptocompare.com/data/v2/histominute");
                var request = new RestRequest(Method.GET);

                request.AddParameter("api_key", api);
                request.AddParameter("tsym", "USD");

                //fix 1INCH if needed
                if (c == "ONEINCH")
                    request.AddParameter("fsym", "1INCH");
                else
                    request.AddParameter("fsym", c);

                //how many minutes to look back
                request.AddParameter("limit", minutesLookBack);

                //make the GET call
                var response = restClient.Execute(request);

                //Console.WriteLine(response.Content);

                //if service call fails throw exception
                if (response.Content.Contains("Error"))
                {
                    throw new Exception("Error in service call");
                }


                //read reply
                dynamic json = JsonConvert.DeserializeObject(response.Content);

                //counter for y var in [,]
                int j = 0;

                foreach (var x in json.Data.Data)
                {
                    double low = x.low;
                    //Console.WriteLine("Low: " + low);
                    double high = x.high;
                    //Console.WriteLine("High: " + high);

                    double avg = (high + low) / 2;

                    switch (c)
                    {
                        case "ONEINCH":
                            prices[(int)Coins.ONEINCH, j] = avg;
                            break;
                        case "AAVE":
                            prices[(int)Coins.AAVE, j] = avg;
                            break;
                        case "ADA":
                            prices[(int)Coins.ADA, j] = avg;
                            break;
                        case "ALGO":
                            prices[(int)Coins.ALGO, j] = avg;
                            break;
                        case "ATOM":
                            prices[(int)Coins.ATOM, j] = avg;
                            break;
                        case "AVAX":
                            prices[(int)Coins.AVAX, j] = avg;
                            break;
                        case "BAT":
                            prices[(int)Coins.BAT, j] = avg;
                            break;
                        case "BCH":
                            prices[(int)Coins.BCH, j] = avg;
                            break;
                        case "BSV":
                            prices[(int)Coins.BSV, j] = avg;
                            break;
                        case "BTC":
                            prices[(int)Coins.BTC, j] = avg;
                            break;
                        case "BTT":
                            prices[(int)Coins.BTT, j] = avg;
                            break;
                        case "CEL":
                            prices[(int)Coins.CEL, j] = avg;
                            break;
                        case "CHSB":
                            prices[(int)Coins.CHSB, j] = avg;
                            break;
                        case "COMP":
                            prices[(int)Coins.COMP, j] = avg;
                            break;
                        case "CRV":
                            prices[(int)Coins.CRV, j] = avg;
                            break;
                        case "DASH":
                            prices[(int)Coins.DASH, j] = avg;
                            break;
                        case "DCR":
                            prices[(int)Coins.DCR, j] = avg;
                            break;
                        case "DGB":
                            prices[(int)Coins.DGB, j] = avg;
                            break;
                        case "DOGE":
                            prices[(int)Coins.DOGE, j] = avg;
                            break;
                        case "DOT":
                            prices[(int)Coins.DOT, j] = avg;
                            break;
                        case "EGLD":
                            prices[(int)Coins.EGLD, j] = avg;
                            break;
                        case "EOS":
                            prices[(int)Coins.EOS, j] = avg;
                            break;
                        case "ETC":
                            prices[(int)Coins.ETC, j] = avg;
                            break;
                        case "ETH":
                            prices[(int)Coins.ETH, j] = avg;
                            break;
                        case "FIL":
                            prices[(int)Coins.FIL, j] = avg;
                            break;
                        case "FTT":
                            prices[(int)Coins.FTT, j] = avg;
                            break;
                        case "GRT":
                            prices[(int)Coins.GRT, j] = avg;
                            break;
                        case "HBAR":
                            prices[(int)Coins.HBAR, j] = avg;
                            break;
                        case "HT":
                            prices[(int)Coins.HT, j] = avg;
                            break;
                        case "KSM":
                            prices[(int)Coins.KSM, j] = avg;
                            break;
                        case "LINK":
                            prices[(int)Coins.LINK, j] = avg;
                            break;
                        case "LRC":
                            prices[(int)Coins.LRC, j] = avg;
                            break;
                        case "LTC":
                            prices[(int)Coins.LTC, j] = avg;
                            break;
                        case "LUNA":
                            prices[(int)Coins.LUNA, j] = avg;
                            break;
                        case "MIOTA":
                            prices[(int)Coins.MIOTA, j] = avg;
                            break;
                        case "MKR":
                            prices[(int)Coins.MKR, j] = avg;
                            break;
                        case "NANO":
                            prices[(int)Coins.NANO, j] = avg;
                            break;
                        case "NEAR":
                            prices[(int)Coins.NEAR, j] = avg;
                            break;
                        case "NEO":
                            prices[(int)Coins.NEO, j] = avg;
                            break;
                        case "OMG":
                            prices[(int)Coins.OMG, j] = avg;
                            break;
                        case "ONT":
                            prices[(int)Coins.ONT, j] = avg;
                            break;
                        case "QNT":
                            prices[(int)Coins.QNT, j] = avg;
                            break;
                        case "REN":
                            prices[(int)Coins.REN, j] = avg;
                            break;
                        case "RENBTC":
                            prices[(int)Coins.RENBTC, j] = avg;
                            break;
                        case "RUNE":
                            prices[(int)Coins.RUNE, j] = avg;
                            break;
                        case "SC":
                            prices[(int)Coins.SC, j] = avg;
                            break;
                        case "SNX":
                            prices[(int)Coins.SNX, j] = avg;
                            break;
                        case "SOL":
                            prices[(int)Coins.SOL, j] = avg;
                            break;
                        case "SUSHI":
                            prices[(int)Coins.SUSHI, j] = avg;
                            break;
                        case "THETA":
                            prices[(int)Coins.THETA, j] = avg;
                            break;
                        case "TRX":
                            prices[(int)Coins.TRX, j] = avg;
                            break;
                        case "UMA":
                            prices[(int)Coins.UMA, j] = avg;
                            break;
                        case "UNI":
                            prices[(int)Coins.UNI, j] = avg;
                            break;
                        case "VET":
                            prices[(int)Coins.VET, j] = avg;
                            break;
                        case "VGX":
                            prices[(int)Coins.VGX, j] = avg;
                            break;
                        case "WAVES":
                            prices[(int)Coins.WAVES, j] = avg;
                            break;
                        case "WBTC":
                            prices[(int)Coins.WBTC, j] = avg;
                            break;
                        case "XEM":
                            prices[(int)Coins.XEM, j] = avg;
                            break;
                        case "XLM":
                            prices[(int)Coins.XLM, j] = avg;
                            break;
                        case "XMR":
                            prices[(int)Coins.XMR, j] = avg;
                            break;
                        case "XRP":
                            prices[(int)Coins.XRP, j] = avg;
                            break;
                        case "XTZ":
                            prices[(int)Coins.XTZ, j] = avg;
                            break;
                        case "YFI":
                            prices[(int)Coins.YFI, j] = avg;
                            break;
                        case "ZEC":
                            prices[(int)Coins.ZEC, j] = avg;
                            break;
                        case "ZEN":
                            prices[(int)Coins.ZEN, j] = avg;
                            break;
                        case "ZIL":
                            prices[(int)Coins.ZIL, j] = avg;
                            break;
                        case "ZRX":
                            prices[(int)Coins.ZRX, j] = avg;
                            break;
                    }

                    //Debug.WriteLine("adding price " + avg);

                    j++;
                }

                //i++;
            }

            return prices;

        }

        static double getPercent(double[,] prices, int minutes, string coin)
        {
            double prevPrice;
            double currPrice;

            switch (coin)
            {
                case "ONEINCH":
                    prevPrice = prices[(int)Coins.ONEINCH, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ONEINCH, minutesLookBack];
                    break;
                case "AAVE":
                    prevPrice = prices[(int)Coins.AAVE, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.AAVE, minutesLookBack];
                    break;
                case "ADA":
                    prevPrice = prices[(int)Coins.ADA, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ADA, minutesLookBack];
                    break;
                case "ALGO":
                    prevPrice = prices[(int)Coins.ALGO, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ALGO, minutesLookBack];
                    break;
                case "ATOM":
                    prevPrice = prices[(int)Coins.ATOM, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ATOM, minutesLookBack];
                    break;
                case "AVAX":
                    prevPrice = prices[(int)Coins.AVAX, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.AVAX, minutesLookBack];
                    break;
                case "BAT":
                    prevPrice = prices[(int)Coins.BAT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.BAT, minutesLookBack];
                    break;
                case "BCH":
                    prevPrice = prices[(int)Coins.BCH, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.BCH, minutesLookBack];
                    break;
                case "BSV":
                    prevPrice = prices[(int)Coins.BSV, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.BSV, minutesLookBack];
                    break;
                case "BTC":
                    prevPrice = prices[(int)Coins.BTC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.BTC, minutesLookBack];
                    break;
                case "BTT":
                    prevPrice = prices[(int)Coins.BTT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.BTT, minutesLookBack];
                    break;
                case "CEL":
                    prevPrice = prices[(int)Coins.CEL, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.CEL, minutesLookBack];
                    break;
                case "CHSB":
                    prevPrice = prices[(int)Coins.CHSB, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.CHSB, minutesLookBack];
                    break;
                case "COMP":
                    prevPrice = prices[(int)Coins.COMP, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.COMP, minutesLookBack];
                    break;
                case "CRV":
                    prevPrice = prices[(int)Coins.CRV, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.CRV, minutesLookBack];
                    break;
                case "DASH":
                    prevPrice = prices[(int)Coins.DASH, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.DASH, minutesLookBack];
                    break;
                case "DCR":
                    prevPrice = prices[(int)Coins.DCR, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.DCR, minutesLookBack];
                    break;
                case "DGB":
                    prevPrice = prices[(int)Coins.DGB, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.DGB, minutesLookBack];
                    break;
                case "DOGE":
                    prevPrice = prices[(int)Coins.DOGE, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.DOGE, minutesLookBack];
                    break;
                case "DOT":
                    prevPrice = prices[(int)Coins.DOT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.DOT, minutesLookBack];
                    break;
                case "EGLD":
                    prevPrice = prices[(int)Coins.EGLD, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.EGLD, minutesLookBack];
                    break;
                case "EOS":
                    prevPrice = prices[(int)Coins.EOS, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.EOS, minutesLookBack];
                    break;
                case "ETC":
                    prevPrice = prices[(int)Coins.ETC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ETC, minutesLookBack];
                    break;
                case "ETH":
                    prevPrice = prices[(int)Coins.ETH, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ETH, minutesLookBack];
                    break;
                case "FIL":
                    prevPrice = prices[(int)Coins.FIL, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.FIL, minutesLookBack];
                    break;
                case "FTT":
                    prevPrice = prices[(int)Coins.FTT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.FTT, minutesLookBack];
                    break;
                case "GRT":
                    prevPrice = prices[(int)Coins.GRT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.GRT, minutesLookBack];
                    break;
                case "HBAR":
                    prevPrice = prices[(int)Coins.HBAR, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.HBAR, minutesLookBack];
                    break;
                case "HT":
                    prevPrice = prices[(int)Coins.HT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.HT, minutesLookBack];
                    break;
                case "KSM":
                    prevPrice = prices[(int)Coins.KSM, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.KSM, minutesLookBack];
                    break;
                case "LINK":
                    prevPrice = prices[(int)Coins.LINK, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.LINK, minutesLookBack];
                    break;
                case "LRC":
                    prevPrice = prices[(int)Coins.LRC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.LRC, minutesLookBack];
                    break;
                case "LTC":
                    prevPrice = prices[(int)Coins.LTC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.LTC, minutesLookBack];
                    break;
                case "LUNA":
                    prevPrice = prices[(int)Coins.LUNA, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.LUNA, minutesLookBack];
                    break;
                case "MIOTA":
                    prevPrice = prices[(int)Coins.MIOTA, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.MIOTA, minutesLookBack];
                    break;
                case "MKR":
                    prevPrice = prices[(int)Coins.MKR, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.MKR, minutesLookBack];
                    break;
                case "NANO":
                    prevPrice = prices[(int)Coins.NANO, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.NANO, minutesLookBack];
                    break;
                case "NEAR":
                    prevPrice = prices[(int)Coins.NEAR, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.NEAR, minutesLookBack];
                    break;
                case "NEO":
                    prevPrice = prices[(int)Coins.NEO, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.NEO, minutesLookBack];
                    break;
                case "OMG":
                    prevPrice = prices[(int)Coins.OMG, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.OMG, minutesLookBack];
                    break;
                case "ONT":
                    prevPrice = prices[(int)Coins.ONT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ONT, minutesLookBack];
                    break;
                case "QNT":
                    prevPrice = prices[(int)Coins.QNT, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.QNT, minutesLookBack];
                    break;
                case "REN":
                    prevPrice = prices[(int)Coins.REN, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.REN, minutesLookBack];
                    break;
                case "RENBTC":
                    prevPrice = prices[(int)Coins.RENBTC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.RENBTC, minutesLookBack];
                    break;
                case "RUNE":
                    prevPrice = prices[(int)Coins.RUNE, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.RUNE, minutesLookBack];
                    break;
                case "SC":
                    prevPrice = prices[(int)Coins.SC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.SC, minutesLookBack];
                    break;
                case "SNX":
                    prevPrice = prices[(int)Coins.SNX, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.SNX, minutesLookBack];
                    break;
                case "SOL":
                    prevPrice = prices[(int)Coins.SOL, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.SOL, minutesLookBack];
                    break;
                case "SUSHI":
                    prevPrice = prices[(int)Coins.SUSHI, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.SUSHI, minutesLookBack];
                    break;
                case "THETA":
                    prevPrice = prices[(int)Coins.THETA, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.THETA, minutesLookBack];
                    break;
                case "TRX":
                    prevPrice = prices[(int)Coins.TRX, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.TRX, minutesLookBack];
                    break;
                case "UMA":
                    prevPrice = prices[(int)Coins.UMA, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.UMA, minutesLookBack];
                    break;
                case "UNI":
                    prevPrice = prices[(int)Coins.UNI, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.UNI, minutesLookBack];
                    break;
                case "VET":
                    prevPrice = prices[(int)Coins.VET, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.VET, minutesLookBack];
                    break;
                case "VGX":
                    prevPrice = prices[(int)Coins.VGX, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.VGX, minutesLookBack];
                    break;
                case "WAVES":
                    prevPrice = prices[(int)Coins.WAVES, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.WAVES, minutesLookBack];
                    break;
                case "WBTC":
                    prevPrice = prices[(int)Coins.WBTC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.WBTC, minutesLookBack];
                    break;
                case "XEM":
                    prevPrice = prices[(int)Coins.XEM, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.XEM, minutesLookBack];
                    break;
                case "XLM":
                    prevPrice = prices[(int)Coins.XLM, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.XLM, minutesLookBack];
                    break;
                case "XMR":
                    prevPrice = prices[(int)Coins.XMR, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.XMR, minutesLookBack];
                    break;
                case "XRP":
                    prevPrice = prices[(int)Coins.XRP, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.XRP, minutesLookBack];
                    break;
                case "XTZ":
                    prevPrice = prices[(int)Coins.XTZ, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.XTZ, minutesLookBack];
                    break;
                case "YFI":
                    prevPrice = prices[(int)Coins.YFI, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.YFI, minutesLookBack];
                    break;
                case "ZEC":
                    prevPrice = prices[(int)Coins.ZEC, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ZEC, minutesLookBack];
                    break;
                case "ZEN":
                    prevPrice = prices[(int)Coins.ZEN, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ZEN, minutesLookBack];
                    break;
                case "ZIL":
                    prevPrice = prices[(int)Coins.ZIL, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ZIL, minutesLookBack];
                    break;
                case "ZRX":
                    prevPrice = prices[(int)Coins.ZRX, minutesLookBack - minutes];
                    currPrice = prices[(int)Coins.ZRX, minutesLookBack];
                    break;
                default:
                    currPrice = 0;
                    prevPrice = 0;
                    break;
            }

            Console.WriteLine("Current price: " + currPrice);
            Console.WriteLine("Previous price: " + prevPrice);

            double percentChange = (currPrice - prevPrice) / prevPrice * 100;

            return percentChange;
        }


    }
}
