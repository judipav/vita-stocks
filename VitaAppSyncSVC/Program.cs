using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VitaAppSyncSVC
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0].ToString())
                {
                    case "--total": 
                        await LocalStorage.ProcessTotal();
                        break;
                    case "--changes":
                        await LocalStorage.ProcessChanges();
                        break;
                    default:
                        await LocalStorage.ProcessChanges();
                        break;
                }
            }
            else
            {
                await LocalStorage.ProcessChanges();
            }
        }

    }
}
