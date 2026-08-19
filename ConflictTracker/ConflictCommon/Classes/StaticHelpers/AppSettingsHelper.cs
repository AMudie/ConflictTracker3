using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace ConflictCommon.Classes.StaticHelpers
{
    public static class AppSettingsHelper
    {

        public static string LoadAppSetting(string appSettingKey)
        {
            var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

            var value = config[appSettingKey];
            if (value is not null)
            {
                return (config[appSettingKey]).ToString();
            }
            else
            {
                return "";
            }

        }

    }
}
