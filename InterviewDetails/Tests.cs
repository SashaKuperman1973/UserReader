using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace InterviewDetails
{
    [TestFixture]
    public class Tests
    {
        [Test]
        public void PrimaryTest()
        {
            var userReader = new UserReader<User>(new DiContainer(), new Cache());

            Console.WriteLine("First time in");
            User user = userReader.ReadFromPersistence();
            Console.WriteLine("User: " + user);

            Console.WriteLine("Second time in");
            user = userReader.ReadFromPersistence();
            Console.WriteLine("User: " + user);

            Thread.Sleep(3000);

            Console.WriteLine("Third time in, after pause.");
            user = userReader.ReadFromPersistence();
            Console.WriteLine("User: " + user);

            Console.WriteLine("Fourth time in");
            user = userReader.ReadFromPersistence();
            Console.WriteLine("User: " + user);
        }
    }
}
