using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sacknet.KinectFacialRecognitionDemo
{

   

    class CheckinService
    {
        private Dictionary<string, bool> dictionary = null;
        public WebBrowserHelper webBrowserHelper;

        public CheckinService()
        {
            dictionary = new Dictionary<string, bool>();
            
        }

        private void AddPerson(String name) 
        {
            dictionary.Add(name, false);
        }

        public String CheckInCheckOutPerson(String name)
        {
            // See whether Dictionary contains this name.
            if (dictionary.ContainsKey(name))
            {
                bool isPersonCheckedIn = dictionary[name];
                Console.WriteLine(name + " is checked in? " + isPersonCheckedIn);
                dictionary[name] = !isPersonCheckedIn;

                String msg = dictionary[name] ? "in" : "out";

                if (!isPersonCheckedIn)
                {
                    //webBrowserHelper.login(name);
                    webBrowserHelper.checkIn(name);
                }
                else
                {
                    //webBrowserHelper.login(name);
                    webBrowserHelper.checkOut(name);
                }

               return "Thanks " + name + ", you are now succesfully checked " + msg;

            }
            else
            {
                AddPerson(name);
                return CheckInCheckOutPerson(name);
            }
        }
	   
    }
}
