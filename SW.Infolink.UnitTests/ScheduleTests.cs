using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Infolink.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.UnitTests
{
    [TestClass]
    public class ScheduleTests
    {

        [TestMethod]
        public void TestDaily()
        {
            //TimeSpan[] tsarraydaily =
            //{
            //    TimeSpan.FromHours(5),
            //    TimeSpan.FromHours(3)
            //};

            var s = new Schedule(Recurrence.Daily, TimeSpan.FromHours(3));

            var n = s.Next();

//            TimeSpan[] tsarraydaily1 =
//{
//                TimeSpan.FromHours(5),
//                TimeSpan.FromHours(23)
//            };

            var s1 = new Schedule(Recurrence.Daily, TimeSpan.FromHours(5));

            var n1 = s1.Next();
            //var settings = services.GetService<InfolinkSettings>();

            //Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void TestHourly()
        {
            //TimeSpan[] tsarraydaily =
            //{
            //    TimeSpan.FromMinutes(5),
            //    TimeSpan.FromMinutes(45)
            //};

            var s = new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(45));

            var n = s.Next();

//            TimeSpan[] tsarraydaily1 =
//{
//                TimeSpan.FromMinutes(5),
//                TimeSpan.FromMinutes(23)
//            };

            var s1 = new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(5));

            var n1 = s1.Next();
            //var settings = services.GetService<InfolinkSettings>();

            //Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void TestNextSchedule()
        {
//            TimeSpan[] tsarraydaily =
//{
//                TimeSpan.FromHours(5),
//                TimeSpan.FromHours(20)
//            };

            var ds = new Schedule(Recurrence.Daily, TimeSpan.FromHours(20));

            

//            TimeSpan[] tsarrayhourly =
//{
//                TimeSpan.FromMinutes(5),
//                TimeSpan.FromMinutes(23)
//            };

            var hs = new Schedule(Recurrence.Hourly, TimeSpan.FromMinutes(23));

            var sub = new Subscriber("test", 1);

            var nsched = sub.NextSchedule();

            sub.Schedules.Add(ds);
            sub.Schedules.Add(hs);

            var nsched1 = sub.NextSchedule();

        }
    }
}
