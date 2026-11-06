using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Iris {
    internal static class L {
        static readonly Lazy<Dictionary<string,string>> English=new Lazy<Dictionary<string,string>>(ReadEnglish);
        static string current="de";
        public static string Language {get{return current;}}
        public static CultureInfo Culture {get{return CultureInfo.GetCultureInfo(Language=="en"?"en-US":"de-DE");}}
        public static void Set(string language){current=language=="en"?"en":"de";}
        public static string T(string value){
            if(Language!="en"||string.IsNullOrEmpty(value))return value;
            string translated;
            return English.Value.TryGetValue(value,out translated)?translated:value;
        }
        public static string DisplayDefault(string saved,string currentDefault){
            if(string.IsNullOrEmpty(saved)||saved==currentDefault)return saved;
            string translated;
            if(Language=="en")return T(saved)==currentDefault?currentDefault:saved;
            return English.Value.TryGetValue(currentDefault,out translated)&&translated==saved?currentDefault:saved;
        }
        static Dictionary<string,string> ReadEnglish(){
            var result=new Dictionary<string,string>(StringComparer.Ordinal);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Iris.English"))
            using(var reader=new StreamReader(stream,Encoding.UTF8)){
                string line;
                while((line=reader.ReadLine())!=null){
                    int tab=line.IndexOf('\t');
                    if(tab>0)result.Add(Unescape(line.Substring(0,tab)),Unescape(line.Substring(tab+1)));
                }
            }
            return result;
        }
        static string Unescape(string text){return text.Replace("\\r","\r").Replace("\\n","\n").Replace("\\t","\t").Replace("\\\"","\"").Replace("\\\\","\\");}
    }

}
