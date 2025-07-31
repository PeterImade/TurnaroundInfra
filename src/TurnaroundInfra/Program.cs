using Amazon.CDK;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TurnaroundInfra
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new TurnaroundInfraStack(app, "TurnaroundInfraStack");
            app.Synth();
        }
    }
}
