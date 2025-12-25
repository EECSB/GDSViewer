using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using static System.Net.WebRequestMethods;

namespace GDSViewer.JsInterop
{
    public class KonvaJsInterop
    {
        //Constructors ///////////////////////////////////////////////

        public KonvaJsInterop(IJSRuntime js)
        {
            JS = js;
        }

        //////////////////////////////////////////////////////////////
        

        //Properties /////////////////////////////////////////////////

        public IJSRuntime JS { get; set; }

        //////////////////////////////////////////////////////////////


        //Methods ////////////////////////////////////////////////////

        public ValueTask InitializeKonva() 
        {
            return JS.InvokeVoidAsync("initKonva", new {});
        }
  
        //////////////////////////////////////////////////////////////
    }
}