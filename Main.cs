//
// Driver.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2006 Jb Evain
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.IO;
using System.Xml.XPath;

using Mono.Linker;
using Mono.Linker.Steps;

public class Driver {

	public static int Main (string [] args)
	{
		Driver driver = new Driver ();
		driver.Run ();
		return 0;
	}


	const string PROJ = "new_oldjs";
	void Run ()
	{
		Pipeline p = GetStandardPipeline ();
		LinkContext context = GetDefaultContext (p);

		DirectoryInfo info = new DirectoryInfo (PROJ);
		context.Resolver.AddSearchDirectory (info.FullName);

		foreach (var file in info.GetFiles ())
			p.PrependStep (new ResolveFromAssemblyStep (info.FullName + "/" + file.Name));

		p.Process (context);
	}


	static LinkContext GetDefaultContext (Pipeline pipeline)
	{
		LinkContext context = new LinkContext (pipeline);
		context.CoreAction = AssemblyAction.Skip;
		context.OutputDirectory = "output";
		return context;
	}

	static Pipeline GetStandardPipeline ()
	{
		Pipeline p = new Pipeline ();
		p.AppendStep (new LoadReferencesStep ());
		p.AppendStep (new BlacklistStep ());
		p.AppendStep (new TypeMapStep ());
		p.AppendStep (new MarkStep ());
		return p;
	}
}
