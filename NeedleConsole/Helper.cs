using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NeedleConsole
{
    public class Helper
    {
        #region 格式转换，处理异常
        public static string Tostring(object value)
        {
            string result = string.Empty;
            try
            {
                result = value.ToString();
            }
            catch (Exception e)
            {
                result = string.Empty;
            }
            return result;
        }

        public static Int32 ToInt32(object value)
        {
            Int32 result = 0;
            try
            {
                result = System.Convert.ToInt32(value);
            }
            catch (Exception e)
            {
                result = 0;
            }
            return result;
        }

        public static Int64 ToInt64(object value)
        {
            Int64 result = 0;
            try
            {
                result = System.Convert.ToInt64(value);
            }
            catch (Exception e)
            {
                result = 0;
            }
            return result;
        }
        public static double ToDouble(object value)
        {
            double result = 0;
            try
            {
                result = System.Convert.ToDouble(value);
            }
            catch (Exception e)
            {
                result = 0;
            }
            return result;
        }
        public static decimal ToDecimal(object value)
        {
            decimal result = 0;
            try
            {
                result = System.Convert.ToDecimal(value);
            }
            catch (Exception e)
            {
                result = 0;
            }
            return result;
        }

        public static bool ToBool(object value)
        {
            bool result = false;
            try
            {
                result = System.Convert.ToBoolean(value);
            }
            catch (Exception e)
            {
                result = false;
            }
            return result;
        }

        public static DateTime ToDateTime(object value)
        {
            DateTime result = new DateTime(1990);
            try
            {
                result = System.Convert.ToDateTime(value);
            }
            catch (Exception e)
            {
            }
            return result;
        }
        #endregion
    }
}
