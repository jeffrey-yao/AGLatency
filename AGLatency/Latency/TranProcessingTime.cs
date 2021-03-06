﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.XEvent;
using Microsoft.SqlServer.XEvent.Linq;
using System.Data.SQLite;
using System.IO;

namespace AGLatency.Latency
{
    //recovery_unit_harden_log_timestamps-->processing_time
    public class TranProcessingTime_Sec //every second, how many tran processed and their metrics
    {
        public List<string> _FailedReadFields = new List<string>(); //first field so that GetFields will return it fast.
        public DateTime EventTimeStamp;
        [OutputExclude(true)]
        public Int64 secondDistance;//use to idenity how many seconds passed since the first record
        public Int32 database_id;
        public float Avg_Duration;
        [OutputExclude(true)]//don't want to show sum, since multiple log blocks flush concurrently? is it possible?
        public Int64 Sum_Duration;
        public Int64 Max_Duration;
        public Int64 Min_Duration;
        public Int64 TranCommits;//how many logblocks in this second
        
    }

    class TranProcessingTime 
    {

        public Replica server;
        public EventLatency eventLatency = null;
        public string chartHtml = "";
        public TranProcessingTime( )
        {
           
        }
        
        //Multiple database, need to take care of that. 
      
        public void Register()
        {

            if (server == Replica.Primary)
            {
                eventLatency = new EventLatency("TranProcessingTime_Primary");

                
                eventLatency.primaryEvents.
                    Add(new EventWithMode(EventMetaData.xEvent.recovery_unit_harden_log_timestamps));


                //Add it to xeloader
                XELoader.AddEventLatency(eventLatency);
            }

            else

            {
                Logger.LogMessage("[ERROR]Secondary doesn't have recovery_unit_harden_log_timestamps event. ");
            }
        }
        public void CreatePages()
        {
            var dict = GetPerfPointData(eventLatency.eventDB.SQLiteDBFile);

            foreach (KeyValuePair<int, List<Latency.TranProcessingTime_Sec>> kv in dict)
            {
                Pages.TranProcessingTimePage page =
                    new Pages.TranProcessingTimePage(server, kv.Key, kv.Value,  "Commit (db=" + kv.Key + ")", "Primary Statistics");

                page.GetData();
                // page.SavePageToDisk();

                PageTemplate.PageObject pageObj = new PageTemplate.PageObject("TranProcessingTime ", page, PageTemplate.PageObjState.SaveToDiskOnly);

                Controller.pageObjs.Add(pageObj);

            }
        }


        public Dictionary<int, List<TranProcessingTime_Sec>> GetPerfPointData(string sqliteDBFile)
        {
            Dictionary<int, List<TranProcessingTime_Sec>> dict = new Dictionary<int, List<TranProcessingTime_Sec>>();


            string dbfile = sqliteDBFile;// @"C:\AGLatency\AGLatency\bin\Debug\SQLiteDB\LocalHarden_Primary_2__2018-07-27_22_06_38.175.SQLiteDB";
            SQLiteDB db = new SQLiteDB();
            db.Open(dbfile);

            String databaseNum = "SELECT DISTINCT database_id FROM recovery_unit_harden_log_timestamps WHERE database_id>4";
            SQLiteDataReader dbidDR =
            db.ExecuteReader(databaseNum);

            List<int> databases = new List<int>();

            if (dbidDR == null) return dict;

            while (dbidDR.Read())
            {
                databases.Add(dbidDR.GetInt16(0));
            }

            databases.Sort();


            foreach (int id in databases)
            {
                dict.Add(id, new List<TranProcessingTime_Sec>());
            }

            //    String select = "SELECT   (EventTimeStamp/10000000) as EventTimeStamp,database_id, AVG(duration) as Avg_Duration,SUM(duration) as Sum_Duration, COUNT(*) as Flushes,SUM(write_size) as Sum_write_size from log_flush_complete group by  EventTimeStamp/10000000,database_id ORDER BY EventTimeStamp / 10000000,database_id";
            String select = "SELECT   (EventTimeStamp/10000000) as EventTimeStamp,database_id, COUNT(*) as TranCommits, AVG(processing_time) as Avg_Duration,SUM(processing_time) as Sum_Duration, max(processing_time) as Max_duration, min(processing_time) as Min_duration  from recovery_unit_harden_log_timestamps WHERE database_id>4 group by  EventTimeStamp/10000000,database_id ORDER BY EventTimeStamp / 10000000,database_id";

            SQLiteDataReader dr =
            db.ExecuteReader(select);

            if (dr == null) return dict;



            bool isFirst = true;

            Int64 firstTimeStamp = 0;
            while (dr.Read())
            {

                TranProcessingTime_Sec pfp = new TranProcessingTime_Sec();
                Int64 EventTimeStamp = dr.GetInt64(0) + 1;//Add one more second , say, 1.220 should be mapped to 2.00 

                pfp.EventTimeStamp = new DateTime(EventTimeStamp * 10000000);
                pfp.database_id = dr.GetInt32(1);
                pfp.TranCommits = dr.GetInt64(2);
                //need to /1000 then=ms
                pfp.Avg_Duration = Math.Max(0, dr.GetFloat(3)/1000);
                pfp.Sum_Duration = Math.Max(0, dr.GetInt64(4)/1000);
                pfp.Max_Duration = Math.Max(0, dr.GetInt64(5)/1000);
                pfp.Min_Duration = Math.Max(0, dr.GetInt64(6)/1000);

             


                if (isFirst)
                {
                    firstTimeStamp = EventTimeStamp;

                    isFirst = false;
                }



                pfp.secondDistance = EventTimeStamp - firstTimeStamp;

                dict[pfp.database_id].Add(pfp);


            }



            db.CloseConnection();

            return dict;
        }
        


         


    }
}
