using System.Text;

namespace GdsII.Cli
{
    ///<summary>
    ///The entry point, and nothing else.
    ///
    ///Everything the tool does is in <see cref="Cli"/>, which takes its output as writers and returns an
    ///exit code rather than touching Console or Environment. That is what lets the commands be tested
    ///without a process: a test calls Run with a StringWriter and reads what came out.
    ///</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            //A layer name or a structure name can be any text, and on Windows the console starts on a
            //legacy code page that would replace anything outside it with a question mark. Best effort:
            //this throws when output is redirected in some hosts, and a redirect does not need it anyway.
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch { }

            return Cli.Run(args, Console.Out, Console.Error);
        }
    }
}
