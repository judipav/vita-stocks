using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using System.Configuration;
using System.Data.Odbc;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Web.Script.Serialization;
using Newtonsoft.Json;
using NLog; 

namespace VitaAppSyncSVC
{
    static class LocalStorage
    {
        static bool saveFailed = false;
        static DataTable failed = new DataTable();
        static Logger logger = LogManager.GetCurrentClassLogger();
        static List<Stocks> failedStocks;
        public static List<List<Stock>> PrepareData2MobApp()
        {
            DataTable changes = GetChanges();
            failed = changes.Clone();
            List<Stock> st = new List<Stock>();
            foreach (DataRow row in changes.Rows)
            {
                Stock x = new Stock();
                x.available_count = (!Convert.IsDBNull(row["stock"])) ? Convert.ToInt32(row["stock"]) : 0;
                x.item_uuid = row["id_mp"].ToString();
                x.store_uuid = row["id_apt"].ToString();
                x.price = (!Convert.IsDBNull(row["price"])) ? Convert.ToSingle(row["price"]) : 0;
                x.price_crossed = (!Convert.IsDBNull(row["max_price"])) ? Convert.ToSingle(row["max_price"]) : 0;
                st.Add(x);
            }
            List<List<Stock>> ls = new List<List<Stock>>();
            ls.AddRange(st.Split(500));
            return ls;
        }
        public static async Task<List<Stocks>> PostDataMobileApp(List<List<Stock>> ls)
        {
            List<Stocks> failed = new List<Stocks>();
            int counter = 1;
            logger.Info("Пакетов к отправке - ({0})",ls.Count);
            foreach (List<Stock> lis in ls)
            {
                counter++;
                await PostDataPacketAsync(failed, counter, lis);
            }
            logger.Info("Пакетов отправлено - ({0})", ls.Count);
            if (failed.Count>0)
            {
                logger.Warn("Неудачно - ({0})", ls.Count);
            }
            return failed;
        }
        public static async Task<List<Stocks>> ParallelPostDataMobileApp(List<List<Stock>> ls)
        {
            List<Stocks> failed = new List<Stocks>();
            int counter = 1;
            logger.Info("Пакетов к отправке - ({0})", ls.Count);
            List<Task> lt = new List<Task>();
            List<List<List<Stock>>> lls = new List<List<List<Stock>>>();
            lls = ls.Split(100);
            foreach (List<List<Stock>> l_s in lls)
            {
                foreach (List<Stock> lis in l_s)
                {
                    counter++;
                    lt.Add(PostDataPacketAsync(failed, counter, lis));
                    
                }
                await Task.WhenAll(lt);
                System.Threading.Thread.Sleep(500);
            }

            logger.Info("Пакетов отправлено - ({0})", ls.Count);
            if (failed.Count > 0)
            {
                logger.Warn("Неудачно - ({0})", ls.Count);
            }
            return failed;
        }
        private static async Task PostDataPacketAsync(List<Stocks> failed, int counter, List<Stock> lis)
        {
            try
            {
                Stocks stocks = new Stocks() { list = lis.ToArray() };
                string jsonObject = JsonConvert.SerializeObject(stocks);
                var content = new StringContent(jsonObject, Encoding.UTF8, "application/json");
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://vita-api.vigroup.ru/"); // v1/items/suggestions/  |||  POST /exch/v1/prices -H auth_token
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("auth_token", "***");
                HttpResponseMessage httpResponse = await client.PostAsync("/exch/v1/prices", content);
                httpResponse.EnsureSuccessStatusCode();
                Console.WriteLine("Packet \t{0}\tOK", counter);
            }
            catch (HttpRequestException ex)
            {
                logger.Error("Сервер мобильного приложения отрыгнул(");
                logger.Error(ex.Message);
                Console.WriteLine("Packet \t{0}\tFAIL", counter);
                failed.Add(new Stocks() { list = lis.ToArray() });
            }
        }
        private static async Task CheckMobAppServerAvailability()
        {
            try
            {
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://vita-api.vigroup.ru/"); // v1/items/suggestions/  |||  POST /exch/v1/prices -H auth_token
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("auth_token", "***");
                HttpResponseMessage httpResponse = await client.GetAsync("/exch/v1/prices");
                httpResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                logger.Error("Сервер мобильного приложения отрыгнул(");
                logger.Error(ex.Message);
                Console.Write("Сервер мобильного приложения отрыгнул(");
                Console.WriteLine(ex.Message);
                throw;
            }
        }
        public static async Task PostFailedDataMobileAppAsync(List<Stocks> failed)
        {
            if (failed.Count > 0)
            {
                logger.Info("Попытка отправить повторно - ({0}) неудавшихся пакетов", failed.Count);
                Console.WriteLine("Попытка отправить повторно - ({0}) неудавшихся пакетов", failed.Count);
                int delta = 0;
                foreach (Stocks failedStock in failed)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            var content = new StringContent(JsonConvert.SerializeObject(failedStock).ToString(), Encoding.UTF8, "application/json");
                            HttpClient client = new HttpClient();
                            client.BaseAddress = new Uri("https://vita-api.vigroup.ru/"); // v1/items/suggestions/  |||  POST /exch/v1/prices -H auth_token
                            client.DefaultRequestHeaders.Accept.Clear();
                            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            client.DefaultRequestHeaders.Add("auth_token", "***");
                            HttpResponseMessage response = await client.PostAsync("/exch/v1/prices", content);
                            response.EnsureSuccessStatusCode();
                            failed.Remove(failedStock);
                            delta++;
                            i = 10;
                            break;
                        }
                        catch (HttpRequestException)
                        {
                            System.Threading.Thread.Sleep(200);
                        }
                    }
                    if (delta == 0)
                    {
                        saveFailed = true;
                        break;
                    }
                }
            }
        }
        public static void DumpDataBus()
        {
            var appSettings = ConfigurationManager.AppSettings;
            string pgDSN = appSettings.Get("PgDSN");
            string pgUID = appSettings.Get("PgUID");
            string pgPWD = appSettings.Get("PgPWD");
            string pgConnectionString = "DSN=" + pgDSN + ";UID=" + pgUID + ";PWD=" + pgPWD + ";QT=-1;LT=-1";
            logger.Info("Получение остатков из шины данных");
            DateTime startTime, endTime;
            startTime = DateTime.Now;
            using (OdbcConnection pgConn = new OdbcConnection(pgConnectionString))
            {
                logger.Debug("Подключаюсь к шине данных.");
                pgConn.Open();
                OdbcCommand pgCommandReader = new OdbcCommand("SELECT " +
                        "\"ID_MP\", \"MARKDOWN\", \"ID_APT\", \"CPOS\", \"STOCK\", \"DOC_TIMESTAMP\"" +
                        "FROM PUBLIC.\"PDC_CC_AGG\"" +
                        "WHERE (to_timestamp((\"DOC_TIMESTAMP\")::double precision) > (CURRENT_TIMESTAMP - '23:00:00'::interval)) " +
                        "AND \"MARKDOWN_FLAG\" = false " +
                        "--limit 100", pgConn);
                pgCommandReader.CommandTimeout = 600;
                try
                {
                    logger.Debug("Забираю остатки...");
                    OdbcDataReader pgDumpReader = pgCommandReader.ExecuteReader();
                    using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
                    {
                        syncConn.Open();
                        SqlCommand truncPdc = new SqlCommand("truncate table dbo.pdc_cc_agg", syncConn);
                        truncPdc.ExecuteNonQuery();
                        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(syncConn))
                        {
                            logger.Debug("Сохраняю остатки в свою базу...");
                            bulkCopy.DestinationTableName = "dbo.pdc_cc_agg";
                            bulkCopy.BulkCopyTimeout = 0;
                            try
                            {
                                bulkCopy.WriteToServer(pgDumpReader);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                                logger.Error(ex.Message);
                            }
                            finally
                            {
                                logger.Debug("Отпускаю шину даных.");
                                pgDumpReader.Close();
                            }
                        }
                    }
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    logger.Error("Ошибка подключения к шине данных\n{0}",ex.Message);
                }
                endTime = DateTime.Now;
                long diffInSeconds = (Int32)(endTime - startTime).TotalSeconds;
                logger.Info("Получены остатки из шины данных.");
                Console.WriteLine("PgDumpDataBusStock\t{0} sec.", diffInSeconds);
            }
        }
        public static void DumpPriceBase()
        {
            OracleConnectionStringBuilder oracleConnectionString = new OracleConnectionStringBuilder(System.Configuration.ConfigurationManager.AppSettings["OraConnString"])
            {
                UserID = System.Configuration.ConfigurationManager.AppSettings["OraName"],
                Password = System.Configuration.ConfigurationManager.AppSettings["OraPwd"]
            };
            DateTime startTime, endTime;
            startTime = DateTime.Now;
            endTime = DateTime.Now;
            logger.Info("Получаю актуальные цены из Вита-Системы.");
            using (OracleConnection oraConn = new OracleConnection(oracleConnectionString.ConnectionString))
            {
                logger.Debug("Подключаюсь к базе Вита-Системы...");
                try
                {
                    oraConn.Open();
                    string oraSqlStatement = "select id_mp, id_region, PRICE, MAX_PRICE, DT_CHANGE from www_all_price_v";
                    using (OracleCommand cmd = new OracleCommand(oraSqlStatement, oraConn) { CommandTimeout = 600 })
                    {
                        logger.Debug("Читаю цены...");
                        OracleDataReader oraReader = cmd.ExecuteReader();
                        if (oraReader.HasRows)
                        {
                            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
                            {
                                logger.Debug("Сохраняю цены в свою базу...");
                                syncConn.Open();
                                SqlCommand truncAllPrice = new SqlCommand("truncate table dbo.www_all_price", syncConn);
                                truncAllPrice.ExecuteNonQuery();
                                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(syncConn))
                                {
                                    bulkCopy.DestinationTableName = "dbo.www_all_price";
                                    bulkCopy.BulkCopyTimeout = 0;
                                    try
                                    {
                                        bulkCopy.WriteToServer(oraReader);
                                        endTime = DateTime.Now;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.Message);
                                        throw;
                                    }
                                    finally
                                    {
                                        oraReader.Close();
                                    }
                                }
                            }
                            logger.Info("Получены актуальные цены.");
                        }
                        else
                        {
                            logger.Warn("Не вижу цен в базе Вита-Системы. Возможно перестраивается Вьюха, обратитесь к Бабушкину Алексею.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("Не удалос корректно совершить запрос из БД Oracle.\nОставляю прежние цены, до следующей выгрузки.\n{0}", ex.Message);
                }
                logger.Debug("Отключаюсь от базы Вита-Системы.");
            }
            Console.WriteLine("OraDumpPrice\t\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
        }
        public static void DumpAptW()
        {
            OracleConnectionStringBuilder oracleConnectionString = new OracleConnectionStringBuilder(System.Configuration.ConfigurationManager.AppSettings["OraConnString"])
            {
                UserID = System.Configuration.ConfigurationManager.AppSettings["OraName"],
                Password = System.Configuration.ConfigurationManager.AppSettings["OraPwd"]
            };
            DateTime startTime, endTime;
            startTime = DateTime.Now;
            endTime = DateTime.Now;
            logger.Info("Получаю справочник аптек из Вита-Системы.");
            try
            {
                using (OracleConnection oraConn = new OracleConnection(oracleConnectionString.ConnectionString))
                {
                    logger.Debug("Подключаюсь к базе...");
                    oraConn.Open();
                    string oraSqlStatement = "select id_apt, ID_REGION from ouip_dev.www_apt";
                    using (OracleCommand cmd = new OracleCommand(oraSqlStatement, oraConn) { CommandTimeout = 600 })
                    {
                        OracleDataReader oraReader = cmd.ExecuteReader();
                        if (oraReader.HasRows)
                        {
                            logger.Debug("Читаю справочник...");
                            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
                            {
                                syncConn.Open();
                                SqlCommand truncAllPrice = new SqlCommand("truncate table dbo.www_apt", syncConn);
                                truncAllPrice.ExecuteNonQuery();
                                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(syncConn))
                                {
                                    logger.Debug("Сохраняю себе в базу...");
                                    bulkCopy.DestinationTableName = "dbo.www_apt";
                                    bulkCopy.BulkCopyTimeout = 0;
                                    try
                                    {
                                        bulkCopy.WriteToServer(oraReader);
                                        endTime = DateTime.Now;
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Error("Что-то пошло не так, проверьте доступность базы Вита-Системы и службы обмена Stocks.");
                                        Console.WriteLine(ex.Message);
                                        throw;
                                    }
                                    finally
                                    {
                                        logger.Info("Справочник аптек получен.");
                                        oraReader.Close();
                                        logger.Debug("Отпускаю базу Вита-системы...");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error("Справочник аптек. Ошибка взаимодействия с источником данных.\n{0}", ex.Message);
            }
            long diffInSeconds = (Int32)(endTime - startTime).TotalSeconds;
            Console.WriteLine("OraDumpApt\t\t{0} sec.", diffInSeconds);
        }
        private static DataTable GetChanges()
        {
            DataTable changes = new DataTable("StockChanges");
            logger.Info("Получаю Дифф с последней выгрузки.");
            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                logger.Debug("Подключаюсь к своей базе...");
                try
                {
                    syncConn.Open();
                    DateTime startTime, endTime;
                    startTime = DateTime.Now;
                    SqlCommand prepChangesSP = new SqlCommand("MPIdentChanges", syncConn);
                    prepChangesSP.CommandType = CommandType.StoredProcedure;
                    prepChangesSP.CommandTimeout = 600;
                    prepChangesSP.ExecuteNonQuery();
                    SqlDataAdapter stockChanges = new SqlDataAdapter("SELECT id_mp,id_apt,price,max_price,stock FROM price_changed", syncConn);
                    stockChanges.Fill(changes);
                    endTime = DateTime.Now;
                    Console.WriteLine("StockChangesReceived\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
                }
                catch (Exception ex)
                {
                    logger.Error("Что творится с моей базой?! Проверьте, пожалуйста, говорит - {0}", ex.Message);
                    throw;
                }
            }
            logger.Info("Дифф у меня.");
            return changes;
        }
        public static void ProcessChangesAndSave()
        {
            //  Save processed records to local storage to check for future changes
            DateTime startTime = DateTime.Now;
            DateTime endTime = DateTime.Now;
            logger.Info("Начинаем колдовать с остатками. Сохраняем разницу как последнюю выгрузку.");
            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                try
                {
                    logger.Debug("Подключаюсь к своей базе, абра-кадабра!");
                    syncConn.Open();
                    SqlCommand saveProcsdSP = new SqlCommand("MPUpdateChangedCompleted", syncConn);
                    saveProcsdSP.CommandType = CommandType.StoredProcedure;
                    saveProcsdSP.CommandTimeout = 0;
                    saveProcsdSP.ExecuteNonQuery();
                    endTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    logger.Error("Опять программисты все сломали! Не вижу своей базы. Проверьте, пожалуйста, говорит - {0}", ex.Message);
                    throw;
                }
            }
            logger.Debug("Шалость удалась.");
            Console.WriteLine("SaveChangesToHistory\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
            startTime = DateTime.Now;
            if (saveFailed == false)
            {
                using (SqlConnection syncCon = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
                {
                    logger.Info("Подчищаем очередь на отправку.");
                    try
                    {
                        logger.Debug("Подключаюсь к своей базе...");
                        syncCon.Open();
                        SqlCommand truncChanges = new SqlCommand("TRUNCATE TABLE dbo.price_changed", syncCon);
                        truncChanges.CommandTimeout = 60;
                        truncChanges.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Шеф! Все пропало! Два магнитофона... \n {0}", ex.Message);
                        throw;
                    }
                }
            }
            endTime = DateTime.Now;
            logger.Info("Диффы сохранены в истории");
            Console.WriteLine("TableChangesTruncated\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
        }
        public static void TruncHistory()
        {
            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                logger.Info("Если вы видите это сообщение, то скорее всего комуто захотелось сделать полный Init остатков на стороне Мобильного Приложения. Будет долго");
                syncConn.Open();
                SqlCommand truncHistory = new SqlCommand("TRUNCATE TABLE dbo.price_compl", syncConn);
                truncHistory.CommandTimeout = 60;
                truncHistory.ExecuteNonQuery();
            }
        }
        public static void SaveFailed(List<Stocks> failedStocks)
        {
            foreach (Stocks s in failedStocks)
            {
                foreach (Stock st in s.list.ToArray())
                {
                    DataRow sltRow = failed.NewRow();
                    sltRow["stock"] = st.available_count;
                    sltRow["id_mp"] = Convert.ToDecimal(st.item_uuid);
                    sltRow["id_apt"] = Convert.ToDecimal(st.store_uuid);
                    sltRow["price"] = Convert.ToDecimal(st.price);
                    sltRow["max_price"] = Convert.ToDecimal(st.price_crossed);
                    failed.Rows.Add(sltRow);
                }                
            }
            using (SqlConnection syncCon = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                logger.Info("Подчищаем очередь на отправку.");
                try
                {
                    logger.Debug("Подключаюсь к своей базе...");
                    syncCon.Open();
                    SqlCommand truncChanges = new SqlCommand("TRUNCATE TABLE dbo.price_changed", syncCon);
                    truncChanges.CommandTimeout = 60;
                    truncChanges.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    logger.Error("Шеф! Все пропало! Два магнитофона... \n {0}", ex.Message);
                    throw;
                }
            }
            using (SqlConnection syncConn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                DataTable dt = new DataTable();
                syncConn.Open();
                SqlDataAdapter adapter = new SqlDataAdapter("SELECT [id_mp],[id_apt],[price],[max_price],[stock] FROM [dbo].[price_changed]", syncConn);
                SqlCommandBuilder infStrBld = new SqlCommandBuilder(adapter)
                {
                    QuotePrefix = "[",
                    QuoteSuffix = "]"
                };
                adapter.InsertCommand = infStrBld.GetInsertCommand();
                adapter.InsertCommand.UpdatedRowSource = UpdateRowSource.None;
                adapter.UpdateCommand = infStrBld.GetUpdateCommand();
                adapter.UpdateCommand.UpdatedRowSource = UpdateRowSource.None;
                adapter.DeleteCommand = infStrBld.GetDeleteCommand();
                adapter.DeleteCommand.UpdatedRowSource = UpdateRowSource.None;
                adapter.Fill(dt);
                dt = failed.Copy();
                try
                {
                    adapter.Update(failed);
                    saveFailed = false;
                }
                catch (Exception ex)
                {
                    logger.Error("Не удается сохранить пакеты, которые не удалось передать. Не понимаю как такое происходит но выглядит так, будто нужно запустить синхронизацию с ключом -total. \nВот что пишет {}", ex.Message);
                    Console.WriteLine("Не удается сохранить пакеты, которые не удалось передать. Не понимаю как такое происходит но выглядит так, будто нужно запустить синхронизацию с ключом -total. \nВот что пишет {}", ex.Message);
                }
            }
        }
        public static async Task ProcessTotal() // Полная выгрузка всех данных (1 раз в сутки?)
        {
            logger.Info("Начинаю Total");
            TruncHistory();
            DumpDataBus();
            DumpPriceBase();
            DumpAptW();
            await ExchageData();
            ProcessChangesAndSave();
            logger.Info("Закончился полный Init остатков в мобильное приложение. Запустите Diff");
        }
        public static async Task ProcessChanges()
        {
            logger.Info("=====================================\t\tНачинаю Diff");
            try
            {
                await CheckNotDelivered();
                DumpDataBus();
                DumpPriceBase();
                DumpAptW();
                await ExchageData();
                if (failedStocks.Count == 0 & saveFailed != true)
                {
                    ProcessChangesAndSave();
                }
                else
                {
                    //ProcessChangesAndSave();
                    //SaveFailed(failedStocks);
                }
            }
            catch (Exception ex)
            {
                logger.Error("Проблемы при обмене изменениями. прерываю работу.\n {0}", ex.Message);
            }
            logger.Info("Закончил Diff. Все хорошо.");
        }
        public static async Task ExchageData()
        {
            logger.Info("Обмен с сервером остатков Мобильного приложения.");
            List<List<Stock>> stocks = PrepareData2MobApp();
            DateTime startTime = DateTime.Now;
            failedStocks = await PostDataMobileApp(stocks);
            DateTime endTime = DateTime.Now;
            Console.WriteLine("\nPostDataMobileApp\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
            startTime = DateTime.Now;
            await Task.Run(() => PostFailedDataMobileAppAsync(failedStocks));
            endTime = DateTime.Now;
            Console.WriteLine("\nPostFailedMobileApp\t{0} sec.", (Int32)(endTime - startTime).TotalSeconds);
            logger.Info("Закончил обмен с сервером остатков Мобильного приложения.");
        }
        public static async Task CheckNotDelivered()
        {
            using (SqlConnection syncCon = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["Stocks"].ConnectionString))
            {
                logger.Info("Проверим, есть ли неудавшиеся попытки отправки пакетов с прошлого раза.");
                try
                {
                    logger.Debug("Подключаюсь к своей базе...");
                    syncCon.Open();
                    SqlCommand truncChanges = new SqlCommand("SELECT count(*) FROM dbo.price_changed", syncCon);
                    truncChanges.CommandTimeout = 60;
                    string result = truncChanges.ExecuteScalar().ToString();
                    int count = 0;
                    if (Int32.TryParse(result, out count)) 
                    {
                        if (count > 0)
                        {
                            logger.Info("Кажется есть что отправить с прошлого раза - {0} пакетов.", count);
                            await CheckMobAppServerAvailability();
                            await ExchageData();
                            ProcessChangesAndSave();
                        }
                        else
                        {
                            logger.Info("Последняя отправка прошла без ошибок.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error("Шеф! Все пропало! Два магнитофона... \n {0}", ex.Message);
                    throw;
                }
            }
        }
    }
    
}
