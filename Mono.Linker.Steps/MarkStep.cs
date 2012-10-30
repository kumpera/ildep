//
// MarkStep.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2006 Jb Evain
// (C) 2007 Novell, Inc.
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
using System.Collections.Generic;
using System.Linq;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mono.Linker.Steps {

	public class MarkStep : IStep {

		protected LinkContext _context;

		TypeDefinition currentType;
		FieldDefinition currentField;
		MethodDefinition currentMethod;

		public AnnotationStore Annotations {
			get { return _context.Annotations; }
		}

		public MarkStep ()
		{
		}

		public virtual void Process (LinkContext context)
		{
			_context = context;

			
			foreach (AssemblyDefinition assembly in _context.GetAssemblies ())
				ProcessAssembly (assembly);

			DumpXmp ();
		}

		void ProcessAssembly (AssemblyDefinition assembly)
		{
			foreach (TypeDefinition type in assembly.MainModule.Types)
				 ProcessType (type);
		}

		void ProcessType (TypeDefinition type)
		{
			currentType = type;

			RecordType (type.BaseType);
			RecordType (type.DeclaringType);
			RecordCustomAttributes (type);
			RecordTypeSpecialCustomAttributes (type);
			RecordGenericParameterProvider (type);

			if (type.HasInterfaces) {
				foreach (TypeReference iface in type.Interfaces)
					RecordType (iface);
			}

			if (type.HasFields)
				ProcessFields (type);
			if (type.HasMethods)
				ProcessMethods (type);
			currentType = null;

			if (!type.HasNestedTypes)
				return;
			foreach (var nested in type.NestedTypes)
				ProcessType (nested);
		}

		void ProcessFields (TypeDefinition type)
		{
			foreach (FieldDefinition field in type.Fields) {
				currentField = field;
				RecordTypeRelation (type);
				ProcessField (field);
				currentField = null;
			}
		}

		void ProcessMethods (TypeDefinition type)
		{
			foreach (MethodDefinition method in type.Methods) {
				currentMethod = method;
				RecordTypeRelation (type);
				ProcessMethod (method);
				currentMethod = null;
			}
		}

		void ProcessField (FieldReference reference)
		{
			if (reference.DeclaringType is GenericInstanceType)
				 RecordType (reference.DeclaringType);

			FieldDefinition field = ResolveFieldDefinition (reference);

			if (field == null)
				throw new ResolutionException (reference);

			RecordType (field.DeclaringType);
			RecordType (field.FieldType);
			RecordCustomAttributes (field);
			RecordMarshalSpec (field);
		}

		void ProcessMethod (MethodDefinition method) {
			RecordType (method.DeclaringType);
			RecordCustomAttributes (method);

			RecordGenericParameterProvider (method);

			//FIXME do we care?
			// if (IsPropertyMethod (method))
			// 	RecordProperty (GetProperty (method));
			// else if (IsEventMethod (method))
			// 	MarkEvent (GetEvent (method));

			if (method.HasParameters) {
				foreach (ParameterDefinition pd in method.Parameters) {
					RecordType (pd.ParameterType);
					RecordCustomAttributes (pd);
					RecordMarshalSpec (pd);
				}
			}

			if (method.HasOverrides) {
				foreach (MethodReference ov in method.Overrides)
					RecordMethod (ov);
			}

			RecordMethodSpecialCustomAttributes (method);

			RecordBaseMethods (method);

			RecordType (method.ReturnType);
			RecordCustomAttributes (method.MethodReturnType);
			RecordMarshalSpec (method.MethodReturnType);

			RecordMethodBody (method.Body);
		}

		void RecordMethodBody (MethodBody body)
		{
			if (body == null)
				return;
			foreach (VariableDefinition var in body.Variables)
				RecordType (var.VariableType);

			foreach (ExceptionHandler eh in body.ExceptionHandlers)
				if (eh.HandlerType == ExceptionHandlerType.Catch)
					RecordType (eh.CatchType);

			foreach (Instruction instruction in body.Instructions)
				RecordInstruction (instruction);
		}

		void RecordInstruction (Instruction instruction)
		{
			switch (instruction.OpCode.OperandType) {
			case OperandType.InlineField:
				RecordField ((FieldReference) instruction.Operand);
				break;
			case OperandType.InlineMethod:
				RecordMethod ((MethodReference) instruction.Operand);
				break;
			case OperandType.InlineTok:
				object token = instruction.Operand;
				if (token is TypeReference)
					RecordType ((TypeReference) token);
				else if (token is MethodReference)
					RecordMethod ((MethodReference) token);
				else
					RecordField ((FieldReference) token);
				break;
			case OperandType.InlineType:
				RecordType ((TypeReference) instruction.Operand);
				break;
			default:
				break;
			}
		}

		void RecordBaseMethods (MethodDefinition method)
		{
			IList base_methods = Annotations.GetBaseMethods (method);
			if (base_methods == null)
				return;

			foreach (MethodDefinition base_method in base_methods)
				RecordMethod (base_method);
		}
	
		void RecordGenericParameter (GenericParameter parameter)
		{
			RecordCustomAttributes (parameter);
			foreach (TypeReference constraint in parameter.Constraints)
				RecordType (constraint);
		}

		void RecordGenericParameterProvider (IGenericParameterProvider provider)
		{
			if (!provider.HasGenericParameters)
				return;

			foreach (GenericParameter parameter in provider.GenericParameters)
				RecordGenericParameter (parameter);
		}

		void RecordMethodSpecialCustomAttributes (MethodDefinition method)
		{
			if (!method.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in method.CustomAttributes) {
				switch (attribute.Constructor.DeclaringType.FullName) {
				case "System.Web.Services.Protocols.SoapHeaderAttribute":
					RecordSoapHeader (method, attribute);
					break;
				}
			}
		}

		void RecordSoapHeader (MethodDefinition method, CustomAttribute attribute)
		{
			string member_name;
			if (!TryGetStringArgument (attribute, out member_name))
				return;

			RecordNamedField (method.DeclaringType, member_name);
			RecordNamedProperty (method.DeclaringType, member_name);
		}

		void RecordNamedField (TypeDefinition type, string field_name)
		{
			if (!type.HasFields)
				return;

			foreach (FieldDefinition field in type.Fields) {
				if (field.Name != field_name)
					continue;

				RecordField (field);
			}
		}

		void RecordNamedProperty (TypeDefinition type, string property_name)
		{
			if (!type.HasProperties)
				return;

			foreach (PropertyDefinition property in type.Properties) {
				if (property.Name != property_name)
					continue;

				RecordMethod (property.GetMethod);
				RecordMethod (property.SetMethod);
			}
		}

		void RecordTypeSpecialCustomAttributes (TypeDefinition type)
		{
			if (!type.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in type.CustomAttributes) {
				switch (attribute.Constructor.DeclaringType.FullName) {
				case "System.Xml.Serialization.XmlSchemaProviderAttribute":
					RecordXmlSchemaProvider (type, attribute);
					break;
				}
			}
		}

		void RecordXmlSchemaProvider (TypeDefinition type, CustomAttribute attribute)
		{
			string method_name;
			if (!TryGetStringArgument (attribute, out method_name))
				return;

			RecordNamedMethod (type, method_name);
		}

		void RecordNamedMethod (TypeDefinition type, string method_name)
		{
			if (!type.HasMethods)
				return;

			foreach (MethodDefinition method in type.Methods) {
				if (method.Name != method_name)
					continue;

				RecordMethod (method);
			}
		}

		void RecordCustomAttributes (ICustomAttributeProvider provider)
		{
			if (!provider.HasCustomAttributes)
				return;

			foreach (CustomAttribute ca in provider.CustomAttributes)
				RecordCustomAttribute (ca);
		}

		void RecordCustomAttribute (CustomAttribute ca)
		{
			RecordMethod (ca.Constructor);

			RecordCustomAttributeArguments (ca);

			TypeReference constructor_type = ca.Constructor.DeclaringType;
			TypeDefinition type = constructor_type.Resolve ();
			if (type == null)
				throw new ResolutionException (constructor_type);

			RecordCustomAttributeProperties (ca, type);
			RecordCustomAttributeFields (ca, type);
		}

		void RecordCustomAttributeArguments (CustomAttribute ca)
		{
			foreach (var argument in ca.ConstructorArguments)
				RecordIfType (argument);
		}

		void RecordCustomAttributeProperties (CustomAttribute ca, TypeDefinition attribute)
		{
			foreach (var named_argument in ca.Properties) {
				PropertyDefinition property = GetProperty (attribute, named_argument.Name);
				if (property != null)
					RecordMethod (property.SetMethod);

				RecordIfType (named_argument.Argument);
			}
		}

		void RecordCustomAttributeFields (CustomAttribute ca, TypeDefinition attribute)
		{
			foreach (var named_argument in ca.Fields) {
				FieldDefinition field = GetField (attribute, named_argument.Name);
				if (field != null)
					RecordField (field);

				RecordIfType (named_argument.Argument);
			}
		}

		void RecordMarshalSpec (IMarshalInfoProvider spec)
		{
			if (!spec.HasMarshalInfo)
				return;

			var marshaler = spec.MarshalInfo as CustomMarshalInfo;
			if (marshaler == null)
				return;

			RecordType (marshaler.ManagedType);
		}

		void RecordModifierType (IModifierType mod)
		{
			RecordType (mod.ModifierType);
		}

		void RecordMethodsIf (ICollection methods, MethodPredicate predicate)
		{
			foreach (MethodDefinition method in methods)
				if (predicate (method))
					RecordMethod (method);
		}

		void RecordIfType (CustomAttributeArgument argument)
		{
			if (argument.Type.FullName != "System.Type")
				return;

			RecordType (argument.Type);
			RecordType ((TypeReference) argument.Value);
		}

		void RecordGenericArguments (IGenericInstance instance)
		{
			foreach (TypeReference argument in instance.GenericArguments)
				RecordType (argument);

			RecordGenericArgumentConstructors (instance);
		}

		void RecordGenericArgumentConstructors (IGenericInstance instance)
		{
			var arguments = instance.GenericArguments;

			var generic_element = GetGenericProviderFromInstance (instance);
			if (generic_element == null)
				return;

			var parameters = generic_element.GenericParameters;

			if (arguments.Count != parameters.Count)
				return;

			for (int i = 0; i < arguments.Count; i++) {
				var argument = arguments [i];
				var parameter = parameters [i];

				if (!parameter.HasDefaultConstructorConstraint)
					continue;

				var argument_definition = ResolveTypeDefinition (argument);
				if (argument_definition == null)
					continue;

				RecordMethodsIf (argument_definition.Methods, ctor => !ctor.IsStatic && !ctor.HasParameters);
			}
		}

		TypeReference RecordOriginalType (TypeReference type)
		{
			while (type is TypeSpecification) {
				GenericInstanceType git = type as GenericInstanceType;
				if (git != null)
					RecordGenericArguments (git);

				var mod = type as IModifierType;
				if (mod != null)
					RecordModifierType (mod);

				type = ((TypeSpecification) type).ElementType;
			}

			return type;
		}

		string CurrentContext {
			get {
				if (currentField != null)
					return string.Format ("f:{0}:{1}:{2}", currentType.Module.Name, currentType.FullName, currentField.Name);
				if (currentMethod != null)
					return string.Format ("m:{0}:{1}:{2}/{3}", currentType.Module.Name, currentType.FullName, currentMethod.Name, currentMethod.HasParameters ? currentMethod.Parameters.Count : 0);
				return string.Format ("t:{0}:{1}", currentType.Module.Name, currentType.FullName);
			}
		}

		class Node {
			static int counter;
			public int Id { get; protected set; }
			public string Label { get; protected  set; }
			public string Module { get; protected  set; }
			public string Container { get; protected  set; }
			public HashSet<Node> PointsTo { get; protected  set; }
			object underlyingObject;

			public override bool Equals (object obj)
			{
				Node n = obj as Node;
				return n != null && n.underlyingObject.Equals (this.underlyingObject);
			}

			public override int GetHashCode ()
			{
				return underlyingObject.GetHashCode ();
			}

			static String Massage (string str)
			{
				return str
					.Replace ('\\', '_')
					.Replace ('/', '_')
					.Replace ('<', '_')
					.Replace ('>', '_');
			}

			public Node (TypeDefinition type)
			{
				Id = counter++;
				Label = Massage (type.FullName);
				Module = type.Module.Name;
				if (type.DeclaringType != null)
					Container = type.DeclaringType.FullName;
				PointsTo = new HashSet<Node> ();
				underlyingObject = type;
			}

			public Node (FieldDefinition field)
			{
				Id = counter++;
				Label = Massage (string.Format("{0}:{1}", field.DeclaringType.FullName, field.Name));
				Module = field.DeclaringType.Module.Name;
				Container = field.DeclaringType.FullName;
				PointsTo = new HashSet<Node> ();
				underlyingObject = field;
			}

			public Node (MethodDefinition method)
			{
				Id = counter++;
				Label = Massage (string.Format("{0}:{1}/{2}", method.DeclaringType.FullName, method.Name, method.HasParameters ? method.Parameters.Count : 0));
				Module = method.DeclaringType.Module.Name;
				Container = method.DeclaringType.FullName;
				PointsTo = new HashSet<Node> ();
				underlyingObject = method;
			}
		}

		Dictionary <TypeDefinition, Node> type_to_node = new Dictionary <TypeDefinition, Node> ();
		Dictionary <FieldDefinition, Node> field_to_node = new Dictionary <FieldDefinition, Node> ();
		Dictionary <MethodDefinition, Node> method_to_node = new Dictionary <MethodDefinition, Node> ();

		Node NodeForField (FieldDefinition field)
		{
			if (field_to_node.ContainsKey (field))
				return field_to_node [field];
			return field_to_node [field] = new Node (field);
		}

		Node NodeForMethod (MethodDefinition method)
		{
			if (method_to_node.ContainsKey (method))
				return method_to_node [method];
			return method_to_node [method] = new Node (method);
		}

		Node NodeForType (TypeDefinition type)
		{
			if (type_to_node.ContainsKey (type))
				return type_to_node [type];
			return type_to_node [type] = new Node (type);
		}

		Node CurrentNode {
			get {
				if (currentField != null)
					return NodeForField (currentField);
				if (currentMethod != null)
					return NodeForMethod (currentMethod);
				return NodeForType (currentType);
			}
		}

		bool dump_graph = false;

		void RecordTypeRelation (TypeDefinition type)
		{
			if (!CurrentNode.PointsTo.Add (NodeForType (type)))
				return;
			if (!dump_graph)
				Console.WriteLine ("t:{0}:{1} | {2}", type.Module.Name, type.FullName, CurrentContext);
		}

		void RecordFieldRelation (FieldDefinition field)
		{
			if (!CurrentNode.PointsTo.Add (NodeForField (field)))
				return;
			if (!dump_graph)
				Console.WriteLine ("f:{0}:{1}:{2} | {3}", field.DeclaringType.Module.Name, field.DeclaringType.FullName, field.Name, CurrentContext);
		}

		void RecordMethodRelation (MethodDefinition method)
		{
			if (!CurrentNode.PointsTo.Add (NodeForMethod (method)))
				return;
			if (!dump_graph)
				Console.WriteLine ("m:{0}:{1}:{2}/{3} | {4}", method.DeclaringType.Module.Name, method.DeclaringType.FullName, method.Name, method.HasParameters ? method.Parameters.Count : 0, CurrentContext);
		}

		void DumpNodes (IEnumerable <Node> nodes)
		{
			foreach (var n in nodes)
				Console.WriteLine ("\t\t<node id=\"{0}\" label=\"{1}\"/>", n.Id, n.Label);
		}

		int edge_count;

		void DumpEdges (IEnumerable <Node> nodes)
		{
			foreach (var n in nodes) {
				foreach (var t in n.PointsTo) {
					Console.WriteLine ("\t\t<edge id=\"{0}\" source=\"{1}\" target=\"{2}\"/>", edge_count++, n.Id, t.Id);
				}
			}
		}

		void DumpXmp ()
		{
			if (!dump_graph)
				return;
			Console.WriteLine ("<?xml version=\"1.0\" encoding=\"UTF8\"?>");
			Console.WriteLine ("<gexf xmlns=\"http://www.gexf.net/1.2draft\"");
			Console.WriteLine ("\txmlns:xsi=\"http://www.w3.org/2001/XMLSchemainstance\"");
			Console.WriteLine ("\txsi:schemaLocation=\"http://www.gexf.net/1.2draft");
			Console.WriteLine ("\t\t\t\thttp://www.gexf.net/1.2draft/gexf.xsd\"");
			Console.WriteLine ("\tversion=\"1.2\">");
			Console.WriteLine ("<graph defaultedgetype=\"directed\">");
			Console.WriteLine ("\t<nodes>");
			DumpNodes (type_to_node.Values);
			DumpNodes (field_to_node.Values);
			DumpNodes (method_to_node.Values);
			Console.WriteLine ("\t</nodes>");
			Console.WriteLine ("\t<edges>");
			DumpEdges (type_to_node.Values);
			DumpEdges (field_to_node.Values);
			DumpEdges (method_to_node.Values);
			Console.WriteLine ("\t</edges>");
			Console.WriteLine ("</graph>");
			Console.WriteLine ("</gexf>");
		}


		void RecordType (TypeReference reference)
		{
			if (reference == null)
				return;

			reference = RecordOriginalType (reference);

			if (reference is GenericParameter)
				return;

			TypeDefinition type = ResolveTypeDefinition (reference);

			if (type == null)
				throw new ResolutionException (reference);

			if (currentType != type)
				RecordTypeRelation (type);
		}

		void RecordField (FieldReference reference)
		{
			if (reference.DeclaringType is GenericInstanceType)
				RecordType (reference.DeclaringType);

			FieldDefinition field = ResolveFieldDefinition (reference);

			if (field == null)
				throw new ResolutionException (reference);

			if (currentField != field)
				RecordFieldRelation (field);
		}

		void RecordMethod (MethodReference reference)
		{
			reference = GetOriginalMethod (reference);

			if (reference.DeclaringType is ArrayType)
				return;

			if (reference.DeclaringType is GenericInstanceType)
				RecordType (reference.DeclaringType);

			MethodDefinition method = ResolveMethodDefinition (reference);

			if (method == null)
				throw new ResolutionException (reference);
			if (method != currentMethod)
				RecordMethodRelation (method);
		}

		PropertyDefinition GetProperty (TypeDefinition type, string propertyname)
		{
			while (type != null) {
				PropertyDefinition property = type.Properties.FirstOrDefault (p => p.Name == propertyname);
				if (property != null)
					return property;

				type = type.BaseType != null ? ResolveTypeDefinition (type.BaseType) : null;
			}

			return null;
		}

		FieldDefinition GetField (TypeDefinition type, string fieldname)
		{
			while (type != null) {
				FieldDefinition field = type.Fields.FirstOrDefault (f => f.Name == fieldname);
				if (field != null)
					return field;

				type = type.BaseType != null ? ResolveTypeDefinition (type.BaseType) : null;
			}

			return null;
		}
		FieldDefinition ResolveFieldDefinition (FieldReference field)
		{
			FieldDefinition fd = field as FieldDefinition;
			if (fd == null)
				fd = field.Resolve ();

			return fd;
		}

		static bool TryGetStringArgument (CustomAttribute attribute, out string argument)
		{
			argument = null;

			if (attribute.ConstructorArguments.Count < 1)
				return false;

			argument = attribute.ConstructorArguments [0].Value as string;

			return argument != null;
		}


		static bool IsSpecialSerializationConstructor (MethodDefinition method)
		{
			if (!IsConstructor (method))
				return false;

			var parameters = method.Parameters;
			if (parameters.Count != 2)
				return false;

			return parameters [0].ParameterType.Name == "SerializationInfo" &&
				parameters [1].ParameterType.Name == "StreamingContext";
		}

		delegate bool MethodPredicate (MethodDefinition method);

		static bool IsConstructor (MethodDefinition method)
		{
			return method.IsConstructor && !method.IsStatic;
		}

		static bool IsStaticConstructor (MethodDefinition method)
		{
			return method.IsConstructor && method.IsStatic;
		}

		protected TypeDefinition ResolveTypeDefinition (TypeReference type)
		{
			TypeDefinition td = type as TypeDefinition;
			if (td == null)
				td = type.Resolve ();

			return td;
		}

		IGenericParameterProvider GetGenericProviderFromInstance (IGenericInstance instance)
		{
			var method = instance as GenericInstanceMethod;
			if (method != null)
				return ResolveMethodDefinition (method.ElementMethod);

			var type = instance as GenericInstanceType;
			if (type != null)
				return ResolveTypeDefinition (type.ElementType);

			return null;
		}

		AssemblyDefinition ResolveAssembly (IMetadataScope scope)
		{
			AssemblyDefinition assembly = _context.Resolve (scope);
			return assembly;
		}

		protected MethodReference GetOriginalMethod (MethodReference method)
		{
			while (method is MethodSpecification) {
				GenericInstanceMethod gim = method as GenericInstanceMethod;
				if (gim != null)
					RecordGenericArguments (gim);

				method = ((MethodSpecification) method).ElementMethod;
			}

			return method;
		}

		MethodDefinition ResolveMethodDefinition (MethodReference method)
		{
			MethodDefinition md = method as MethodDefinition;
			if (md == null)
				md = method.Resolve ();

			return md;
		}

		static bool IsPropertyMethod (MethodDefinition md)
		{
			return (md.SemanticsAttributes & MethodSemanticsAttributes.Getter) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.Setter) != 0;
		}

		static bool IsEventMethod (MethodDefinition md)
		{
			return (md.SemanticsAttributes & MethodSemanticsAttributes.AddOn) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.Fire) != 0 ||
				(md.SemanticsAttributes & MethodSemanticsAttributes.RemoveOn) != 0;
		}

		static PropertyDefinition GetProperty (MethodDefinition md)
		{
			TypeDefinition declaringType = (TypeDefinition) md.DeclaringType;
			foreach (PropertyDefinition prop in declaringType.Properties)
				if (prop.GetMethod == md || prop.SetMethod == md)
					return prop;

			return null;
		}

		static EventDefinition GetEvent (MethodDefinition md)
		{
			TypeDefinition declaringType = (TypeDefinition) md.DeclaringType;
			foreach (EventDefinition evt in declaringType.Events)
				if (evt.AddMethod == md || evt.InvokeMethod == md || evt.RemoveMethod == md)
					return evt;

			return null;
		}

	}
}
