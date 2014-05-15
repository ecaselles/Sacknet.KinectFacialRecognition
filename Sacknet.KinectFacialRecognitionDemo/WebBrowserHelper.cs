using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Navigation;


namespace Sacknet.KinectFacialRecognitionDemo
{

    enum Commands {NoCommand, CheckIn, CheckOut};

    class WebBrowserHelper
    {
        const String SERVER_HOST = "http://www.puck.cc/time/tt.asp";
        const String CMD_LOGIN = "?Command=LoginGo";
        const String CMD_CHECKIN = "?Command=CheckIn";
        const String CMD_CHECKOUT = "?Command=CheckOut";
        const string CMD_LOGOUT = "?Command=LogOut";

        private WebBrowser ws;
        private uint nextCommand;

        public WebBrowserHelper(WebBrowser ws)
        {
            this.ws = ws;
            // Hook the LoadCompleted event for the WebView to know when the URL is fully loaded
            ws.LoadCompleted += new LoadCompletedEventHandler(WebView1_LoadCompleted);
        }

        public bool init()
        {
            this.ws.Navigate(SERVER_HOST);
            return true;
        }


        private bool login(String name)
        {
            String postdata = "Employee=" + name + "&Password=1234";
            System.Text.Encoding a = System.Text.Encoding.UTF8;
            byte[] byte1 = a.GetBytes(postdata);

            this.ws.Navigate(SERVER_HOST + CMD_LOGIN, null, byte1, "Content-Type: application/x-www-form-urlencoded");

            return true;
        }

        public bool checkIn(String name)
        {
            
            login(name);
            nextCommand = (uint)Commands.CheckIn;
            //this.ws.Navigate(SERVER_HOST + CMD_CHECKIN);
            return true;
        }

        public bool checkOut(String name)
        {
            login(name);
            nextCommand = (uint)Commands.CheckOut;
            //this.ws.Navigate(SERVER_HOST + CMD_CHECKOUT);
            return true;
        }

        public bool logOut()
        {
            this.ws.Navigate(SERVER_HOST + CMD_LOGOUT);
            return true;
        }

        public void WebView1_LoadCompleted(object sender, NavigationEventArgs e)
        {
            if (nextCommand == (uint)Commands.CheckIn)
            {
                this.ws.Navigate(SERVER_HOST + CMD_CHECKIN);
            }
            else if(nextCommand == (uint) Commands.CheckOut)
            {
                this.ws.Navigate(SERVER_HOST + CMD_CHECKOUT);
            }

            nextCommand = (uint)Commands.NoCommand;
        }
    }
}
