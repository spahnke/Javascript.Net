////////////////////////////////////////////////////////////////////////////////////////////////////
// File: JavascriptInterop.cpp
// 
// Copyright 2010 Noesis Innovation Inc. All rights reserved.
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
//       notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
//       copyright notice, this list of conditions and the following
//       disclaimer in the documentation and/or other materials provided
//       with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
////////////////////////////////////////////////////////////////////////////////////////////////////

#include <vcclr.h>

#include "JavascriptInterop.h"

#include "SystemInterop.h"
#include "JavascriptException.h"
#include "JavascriptExternal.h"
#include "JavascriptFunction.h"

#include <string>

////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Noesis { namespace Javascript {

////////////////////////////////////////////////////////////////////////////////////////////////////

using namespace std;
using namespace System::Collections;
using namespace System::Collections::Generic;
using namespace System::Reflection;

////////////////////////////////////////////////////////////////////////////////////////////////////

Handle<ObjectTemplate>
JavascriptInterop::NewObjectWrapperTemplate()
{
	Handle<ObjectTemplate> result = ObjectTemplate::New(JavascriptContext::GetCurrentIsolate());
	result->SetInternalFieldCount(1);

    NamedPropertyHandlerConfiguration namedPropertyConfig((GenericNamedPropertyGetterCallback) Getter, (GenericNamedPropertySetterCallback) Setter, nullptr, nullptr, (GenericNamedPropertyEnumeratorCallback) Enumerator, Local<Value>(), PropertyHandlerFlags::kOnlyInterceptStrings);
	result->SetHandler(namedPropertyConfig);

    IndexedPropertyHandlerConfiguration indexedPropertyConfig((IndexedPropertyGetterCallback) IndexGetter, (IndexedPropertySetterCallback) IndexSetter);
    result->SetHandler(indexedPropertyConfig);

	return result;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

ConvertedObjects::ConvertedObjects()
{
	objectToConversion = v8::Map::New(JavascriptContext::GetCurrentIsolate());
}

ConvertedObjects::~ConvertedObjects()
{
	size_t n = objectToConversion->Size();
	Local<Array> keys_and_items = objectToConversion->AsArray();
	for (size_t i = 0; i < n; i++) {
		Local<Value> item_i = keys_and_items->Get((uint32_t)i * 2 + 1);
		Local<External> external = Local<External>::Cast(item_i);
		delete (gcroot<System::Object^> *)external->Value();
	}
}

void
ConvertedObjects::AddConverted(v8::Local<v8::Object> o, System::Object^ converted)
{
	Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	Local<Context> context = isolate->GetCurrentContext();
	Local<External> clr_object_wrapped_for_v8 = External::New(isolate, new gcroot<System::Object^>(converted));
	objectToConversion->Set(context, o, clr_object_wrapped_for_v8);
}

System::Object^
ConvertedObjects::GetConverted(v8::Local<v8::Object> o)
{
	Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	Local<Context> context = isolate->GetCurrentContext();
	MaybeLocal<Value> maybe_found = objectToConversion->Get(context, o);
	Local<Value> found = maybe_found.ToLocalChecked();
	if (found->IsUndefined())
		return nullptr;  // haven't seen this JavaScript object before
	Local<External> external = Local<External>::Cast(found);
	gcroot<System::Object^> *object_ptr = (gcroot<System::Object^> *)external->Value();
	System::Object^ converted = *object_ptr;
	return converted;
}


////////////////////////////////////////////////////////////////////////////////////////////////////

System::Object^
JavascriptInterop::ConvertFromV8(Handle<Value> iValue)
{
	ConvertedObjects already_converted;
	return ConvertFromV8(iValue, already_converted);
}

////////////////////////////////////////////////////////////////////////////////////////////////////

System::Object^
JavascriptInterop::ConvertFromV8(Handle<Value> iValue, ConvertedObjects &already_converted)
{
	if (iValue->IsNull() || iValue->IsUndefined())
		return nullptr;
	if (iValue->IsBoolean())
		return gcnew System::Boolean(iValue->BooleanValue(JavascriptContext::GetCurrentIsolate()->GetCurrentContext()).ToChecked());
	if (iValue->IsInt32())
		return gcnew System::Int32(iValue->Int32Value(JavascriptContext::GetCurrentIsolate()->GetCurrentContext()).ToChecked());
	if (iValue->IsNumber())
		return gcnew System::Double(iValue->NumberValue(JavascriptContext::GetCurrentIsolate()->GetCurrentContext()).ToChecked());
	if (iValue->IsString())
		return gcnew System::String((wchar_t*)*String::Value(JavascriptContext::GetCurrentIsolate(), iValue->ToString(JavascriptContext::GetCurrentIsolate())));
	if (iValue->IsArray())
		return ConvertArrayFromV8(iValue, already_converted);
	if (iValue->IsDate())
		return ConvertDateFromV8(iValue);
    if (iValue->IsRegExp())
        return ConvertRegexFromV8(iValue);
	if (iValue->IsFunction())
		return gcnew JavascriptFunction(iValue->ToObject(JavascriptContext::GetCurrentIsolate()), JavascriptContext::GetCurrent());
	if (iValue->IsObject())
	{
		Handle<Object> object = iValue->ToObject(JavascriptContext::GetCurrentIsolate());

		if (object->InternalFieldCount() > 0)
			return UnwrapObject(iValue);
		else
			return ConvertObjectFromV8(object, already_converted);
	}

	return nullptr;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

Handle<Value>
JavascriptInterop::ConvertToV8(System::Object^ iObject)
{
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	if (iObject != nullptr)
	{
		System::Type^ type = iObject->GetType();

		if (type->IsValueType)
		{
			// Common types first.
			if (type == System::Int32::typeid)
				return v8::Int32::New(isolate, safe_cast<int>(iObject));
			if (type == System::Double::typeid)
				return v8::Number::New(isolate, safe_cast<double>(iObject));
			if (type == System::Boolean::typeid)
				return v8::Boolean::New(isolate, safe_cast<bool>(iObject));
			if (type->IsEnum)
			{
				// No equivalent to enum, so convert to a string.
				pin_ptr<const wchar_t> valuePtr = PtrToStringChars(iObject->ToString());
				wchar_t* value = (wchar_t*) valuePtr;
				return v8::String::NewFromTwoByte(isolate, (uint16_t*)value, v8::NewStringType::kNormal).ToLocalChecked();
			}
			else
			{
				if (type == System::Char::typeid)
				{
					uint16_t c = (uint16_t)safe_cast<wchar_t>(iObject);
					return v8::String::NewFromTwoByte(isolate, &c, v8::NewStringType::kNormal, 1).ToLocalChecked();
				}
				if (type == System::Int64::typeid)
					return v8::Number::New(isolate, (double)safe_cast<long long>(iObject));
				if (type == System::Int16::typeid)
					return v8::Int32::New(isolate, safe_cast<short>(iObject));
				if (type == System::SByte::typeid)
					return v8::Int32::New(isolate, safe_cast<signed char>(iObject));
				if (type == System::Byte::typeid)
					return v8::Int32::New(isolate, safe_cast<unsigned char>(iObject));
				if (type == System::UInt16::typeid)
					return v8::Uint32::New(isolate, safe_cast<unsigned short>(iObject));
				if (type == System::UInt32::typeid)
					return v8::Number::New(isolate, safe_cast<unsigned int>(iObject));  // I tried v8::Uint32, but it converted MaxInt to -1.
				if (type == System::UInt64::typeid)
					return v8::Number::New(isolate, (double)safe_cast<unsigned long long>(iObject));
				if (type == System::Single::typeid)
					return v8::Number::New(isolate, safe_cast<float>(iObject));
				if (type == System::Decimal::typeid)
					return v8::Number::New(isolate, (double)safe_cast<System::Decimal>(iObject));
				if (type == System::DateTime::typeid)
					return v8::Date::New(isolate->GetCurrentContext(), SystemInterop::ConvertFromSystemDateTime(safe_cast<System::DateTime^>(iObject))).ToLocalChecked();
			}
		}
		if (type == System::String::typeid)
		{
			pin_ptr<const wchar_t> valuePtr = PtrToStringChars(safe_cast<System::String^>(iObject));
			wchar_t* value = (wchar_t*) valuePtr;
			return v8::String::NewFromTwoByte(isolate, (uint16_t*)value, v8::NewStringType::kNormal).ToLocalChecked();
		}
		if (type->IsArray)
			return ConvertFromSystemArray(safe_cast<System::Array^>(iObject));
        if (type == System::Text::RegularExpressions::Regex::typeid)
            return ConvertFromSystemRegex(safe_cast<System::Text::RegularExpressions::Regex^>(iObject));
		if (System::Delegate::typeid->IsAssignableFrom(type))
			return ConvertFromSystemDelegate(safe_cast<System::Delegate^>(iObject));
	
	
		if (type->IsGenericType)
		{
			if(type->GetGenericTypeDefinition() == System::Collections::Generic::Dictionary::typeid)
			{
				return ConvertFromSystemDictionary(iObject);
			}
			if (type->IsGenericType && (type->GetGenericTypeDefinition() == System::Collections::Generic::List::typeid))
				return ConvertFromSystemList(iObject);
		}


		if (System::Collections::IDictionary::typeid->IsAssignableFrom(type)){
			//Only do this if no fields defined on this type
			if (type->GetFields(System::Reflection::BindingFlags::DeclaredOnly | System::Reflection::BindingFlags::Instance )->Length == 0){
				return ConvertFromSystemDictionary(iObject);
			}
		}

		if (System::Exception::typeid->IsAssignableFrom(type))
		{
			// Converting exceptions to proper v8 Error objects has the advantage that
			// they will come with stack traces.  We tuck the original Exception into
			// the "InnerException" object so we can rethrow it back into C# land
			// if necessary.
			System::Exception ^exception = safe_cast<System::Exception^>(iObject);
			pin_ptr<const wchar_t> valuePtr = PtrToStringChars(safe_cast<System::String^>(exception->Message));
			wchar_t* value = (wchar_t*)valuePtr;
			Handle<v8::Value> error = v8::Exception::Error(v8::String::NewFromTwoByte(isolate, (uint16_t*)value, v8::NewStringType::kNormal).ToLocalChecked());
			Handle<v8::Object> error_o = v8::Handle<v8::Object>::Cast(error);
			Local<String> key = v8::String::NewFromUtf8(isolate, "InnerException", v8::NewStringType::kNormal).ToLocalChecked();
			error_o->Set(isolate->GetCurrentContext(), key, WrapObject(iObject)).ToChecked();
			return error_o;
		}

		return WrapObject(iObject);
	}

	return Null(isolate);
}

////////////////////////////////////////////////////////////////////////////////////////////////////

// TODO: should return Handle<External>
Handle<Object>
JavascriptInterop::WrapObject(System::Object^ iObject)
{
	JavascriptContext^ context = JavascriptContext::GetCurrent();

	if (context != nullptr)
	{
		Handle<ObjectTemplate> templ = context->GetObjectWrapperTemplate();
		v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
		Handle<Object> object = templ->NewInstance(isolate->GetCurrentContext()).ToLocalChecked();
		object->SetInternalField(0, External::New(isolate, context->WrapObject(iObject)));

		return object;
	}

	throw gcnew System::Exception("No context currently active.");
}

////////////////////////////////////////////////////////////////////////////////////////////////////

// TODO: should use Handle<External> iExternal
System::Object^
JavascriptInterop::UnwrapObject(Handle<Value> iValue)
{
	 if (iValue->IsExternal())
	{
		Handle<External> external = Handle<External>::Cast(iValue);
		JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();
		return wrapper->GetObject();
	}

	if (iValue->IsObject())
	{
		Handle<Object> object = iValue->ToObject(JavascriptContext::GetCurrentIsolate());

		if (object->InternalFieldCount() > 0)
		{
			Handle<External> external = Handle<External>::Cast(object->GetInternalField(0));
			JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();
			return wrapper->GetObject();
		}
	}

	return nullptr;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

System::Object^
JavascriptInterop::ConvertArrayFromV8(Handle<Value> iValue, ConvertedObjects &already_converted)
{
	v8::Handle<v8::Array> object = v8::Handle<v8::Array>::Cast(iValue->ToObject(JavascriptContext::GetCurrentIsolate()));
	int length = object->Length();
	cli::array<System::Object^>^ results = gcnew cli::array<System::Object^>(length);

	// Populate the .NET Array with the v8 Array
	for(int i = 0; i < length; i++)
	{
		results->SetValue(ConvertFromV8(object->Get(i), already_converted), i);
	}

	return results;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

System::Object^
JavascriptInterop::ConvertObjectFromV8(Handle<Object> iObject, ConvertedObjects &already_converted)
{
	System::Object ^converted_object = already_converted.GetConverted(iObject);
	if (converted_object == nullptr) {
		v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
		v8::Local<v8::Array> names = iObject->GetPropertyNames();

		unsigned int length = names->Length();
		Dictionary<System::String^, System::Object^>^ results = gcnew Dictionary<System::String^, System::Object^>(length);
		already_converted.AddConverted(iObject, results);
		for (unsigned int i = 0; i < length; i++) {
			v8::Handle<v8::Value> nameKey = v8::Uint32::New(isolate, i);
			v8::Handle<v8::Value> propName = names->Get(nameKey);
			v8::Handle<v8::Value> propValue = iObject->Get(propName);

			// Property "names" may be integers or other types.  However they will
			// generally be strings so continuing to key this dictionary that way is 
			// probably OK.
			System::String^ key = safe_cast<System::String^>(ConvertFromV8(propName, already_converted)->ToString());
			results[key] = ConvertFromV8(propValue, already_converted);
		}
		converted_object = results;
	}

	return converted_object;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

System::DateTime^
JavascriptInterop::ConvertDateFromV8(Handle<Value> iValue)
{
	System::DateTime^ startDate = gcnew System::DateTime(1970, 1, 1, 0, 0, 0, 0, System::DateTimeKind::Utc);
	double milliseconds = iValue->NumberValue(JavascriptContext::GetCurrentIsolate()->GetCurrentContext()).ToChecked();
	System::TimeSpan^ timespan = System::TimeSpan::FromMilliseconds(milliseconds);
    return System::DateTime(timespan->Ticks + startDate->Ticks).ToLocalTime();
}

////////////////////////////////////////////////////////////////////////////////////////////////////

System::Text::RegularExpressions::Regex^
JavascriptInterop::ConvertRegexFromV8(Handle<Value> iValue)
{
    using RegexOptions = System::Text::RegularExpressions::RegexOptions;

    Handle<RegExp> regexp = Handle<RegExp>::Cast(iValue->ToObject(JavascriptContext::GetCurrentIsolate()));
    Local<String> jsPattern = regexp->GetSource();
    RegExp::Flags jsFlags = regexp->GetFlags();

    System::String^ pattern = safe_cast<System::String^>(ConvertFromV8(jsPattern));
    RegexOptions flags = RegexOptions::ECMAScript;
    if (jsFlags & RegExp::Flags::kIgnoreCase)
        flags = flags | RegexOptions::IgnoreCase;
    if (jsFlags & RegExp::Flags::kMultiline)
        flags = flags | RegexOptions::Multiline;

    return gcnew System::Text::RegularExpressions::Regex(pattern, flags);
}

////////////////////////////////////////////////////////////////////////////////////////////////////

v8::Handle<v8::Value>
JavascriptInterop::ConvertFromSystemArray(System::Array^ iArray) 
{
	int lenght = iArray->Length;
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	Local<Context> context = isolate->GetCurrentContext();
	v8::Handle<v8::Array> result = v8::Array::New(isolate);
	
	// Transform the .NET array into a Javascript array 
	for (int i = 0; i < lenght; i++) 
	{
		v8::Handle<v8::Value> key = v8::Int32::New(isolate, i);
		result->Set(context, key, ConvertToV8(iArray->GetValue(i))).ToChecked();
	}

	return result;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

Handle<Value>
JavascriptInterop::ConvertFromSystemRegex(System::Text::RegularExpressions::Regex^ iRegex)
{
    using RegexOptions = System::Text::RegularExpressions::RegexOptions;
    if (!iRegex->Options.HasFlag(RegexOptions::ECMAScript))
        throw gcnew System::Exception("Only regular expressions with the ECMAScript option can be converted.");

    v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
    Local<Context> context = isolate->GetCurrentContext();

    Local<String> pattern = Handle<String>::Cast(ConvertToV8(iRegex->ToString()));
    RegExp::Flags flags = RegExp::Flags::kNone;
    if (iRegex->Options.HasFlag(RegexOptions::IgnoreCase))
        flags = static_cast<RegExp::Flags>(flags | RegExp::Flags::kIgnoreCase);
    if (iRegex->Options.HasFlag(RegexOptions::Multiline))
        flags = static_cast<RegExp::Flags>(flags | RegExp::Flags::kMultiline);

    return RegExp::New(context, pattern, flags).ToLocalChecked();
}

////////////////////////////////////////////////////////////////////////////////////////////////////

v8::Handle<v8::Value>
JavascriptInterop::ConvertFromSystemDictionary(System::Object^ iObject) 
{
	v8::Handle<v8::Object> object = v8::Object::New(JavascriptContext::GetCurrentIsolate());
	System::Collections::IDictionary^ dictionary =  safe_cast<System::Collections::IDictionary^>(iObject);
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	Local<Context> context = isolate->GetCurrentContext();

	for each(System::Object^ keyValue in dictionary->Keys) 
	{
		v8::Handle<v8::Value> key = ConvertToV8(keyValue);
		v8::Handle<v8::Value> val = ConvertToV8(dictionary[keyValue]);
		object->Set(context, key, val).ToChecked();
	} 



	return object;
}	

////////////////////////////////////////////////////////////////////////////////////////////////////

v8::Handle<v8::Value>
JavascriptInterop::ConvertFromSystemList(System::Object^ iObject) 
{
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	v8::Handle<v8::Array> object = v8::Array::New(isolate);
	System::Collections::IList^ list =  safe_cast<System::Collections::IList^>(iObject);

	for(int i = 0; i < list->Count; i++) 
	{
		v8::Handle<v8::Value> key = v8::Int32::New(isolate, i);
		v8::Handle<v8::Value> val = ConvertToV8(list[i]);
		object->Set(key, val);
	} 

	return object;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

v8::Handle<v8::Value>
JavascriptInterop::ConvertFromSystemDelegate(System::Delegate^ iDelegate) 
{
	JavascriptContext^ context = JavascriptContext::GetCurrent();
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	v8::Handle<v8::External> external = v8::External::New(isolate, context->WrapObject(iDelegate));

	v8::Handle<v8::FunctionTemplate> method = v8::FunctionTemplate::New(isolate, DelegateInvoker, external);
	return method->GetFunction();
}

////////////////////////////////////////////////////////////////////////////////////////////////////

void
JavascriptInterop::DelegateInvoker(const FunctionCallbackInfo<Value>& info)
{
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	JavascriptExternal* wrapper = (JavascriptExternal*)v8::Handle<v8::External>::Cast(info.Data())->Value();
	System::Object^ object = wrapper->GetObject();

	System::Delegate^ delegat = static_cast<System::Delegate^>(object);
	cli::array<System::Reflection::ParameterInfo^>^ parametersInfo = delegat->GetType()->GetMethods()[0]->GetParameters();
	int nparams = parametersInfo->Length;

	// As is normal in JavaScript, we ignore excess input parameters, and pad
	// with null if insufficient are supplied.
	int nsupplied = info.Length();
	cli::array<System::Object^>^ args = gcnew cli::array<System::Object^>(nparams);
	ConvertedObjects already_converted;
	for (int i = 0; i < nparams; i++) 
	{
		if (i < nsupplied)
			args[i] = ConvertFromV8(info[i], already_converted);
		else
			args[i] = nullptr;
	}

	// Perform type conversions where possible.
	for (int i = 0; i < args->Length; i++)
	{
		if (args[i] != nullptr)
		{
			System::Type^ paramType = parametersInfo[i]->ParameterType;
			System::Type^ suppliedType = args[i]->GetType();
			if (suppliedType != paramType)
			{
				System::Object^ converted = SystemInterop::ConvertToType(args[i], paramType);
				if (converted != nullptr)  // if conversion fails then leave original type in place so user get appropriate error message
					args[i] = converted;
			}
		}
	}

	System::Object^ ret;
	try
	{
		// invoke
		ret = delegat->DynamicInvoke(args);
	}
	catch(System::Reflection::TargetInvocationException^ exception)
	{
		info.GetReturnValue().Set(HandleTargetInvocationException(exception));
		return;
	}
	catch(System::ArgumentException^)
	{
		// This is what we get when the arguments cannot be converted to match the
		// delegate's requirements.  Its message is all about C# types so I don't
		// pass it on.
		info.GetReturnValue().Set(isolate->ThrowException(JavascriptInterop::ConvertToV8("Argument mismatch")));
		return;
	}
	catch(System::Exception^ exception)
	{
		info.GetReturnValue().Set(isolate->ThrowException(JavascriptInterop::ConvertToV8(exception)));
		return;
	}

	info.GetReturnValue().Set(ConvertToV8(ret));
}

////////////////////////////////////////////////////////////////////////////////////////////////////

bool
JavascriptInterop::IsSystemObject(Handle<Value> iValue)
{
	if (iValue->IsObject())
	{
		Local<Object> object = iValue->ToObject(JavascriptContext::GetCurrentIsolate());
		return (object->InternalFieldCount() > 0);
	}

	return false;
}

////////////////////////////////////////////////////////////////////////////////////////////////////

void
JavascriptInterop::Getter(Local<String> iName, const PropertyCallbackInfo<Value>& iInfo)
{
	wstring name = (wchar_t*) *String::Value(JavascriptContext::GetCurrentIsolate(), iName);
	Handle<External> external = Handle<External>::Cast(iInfo.Holder()->GetInternalField(0));
	JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();
	Handle<Function> function;
	Handle<Value> value;

	// get method
	function = wrapper->GetMethod(name);
	if (!function.IsEmpty()) {
		iInfo.GetReturnValue().Set(function);  // good value or exception
		return;
	}

	// As for GetMethod().
	if (wrapper->GetProperty(name, value)) {
		iInfo.GetReturnValue().Set(value);  // good value or exception
		return;
	}

	// map toString with ToString
	if (wstring((wchar_t*) *String::Value(JavascriptContext::GetCurrentIsolate(), iName)) == L"toString")
	{
		function = wrapper->GetMethod(L"ToString");
		if (!function.IsEmpty()) {
			iInfo.GetReturnValue().Set(function);
			return;
		}
	}

	// member not found
	if ((wrapper->GetOptions() & SetParameterOptions::RejectUnknownProperties) == SetParameterOptions::RejectUnknownProperties) {
		iInfo.GetReturnValue().Set(JavascriptContext::GetCurrentIsolate()->ThrowException(JavascriptInterop::ConvertToV8("Unknown member: " + gcnew System::String((wchar_t*) *String::Value(JavascriptContext::GetCurrentIsolate(), iName)))));
		return;
	}
	iInfo.GetReturnValue().Set(Handle<Value>());
}

////////////////////////////////////////////////////////////////////////////////////////////////////

void
JavascriptInterop::Setter(Local<String> iName, Local<Value> iValue, const PropertyCallbackInfo<Value>& iInfo)
{
	wstring name = (wchar_t*) *String::Value(JavascriptContext::GetCurrentIsolate(), iName);
	Handle<External> external = Handle<External>::Cast(iInfo.Holder()->GetInternalField(0));
	JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();

	// set property
	iInfo.GetReturnValue().Set(wrapper->SetProperty(name, iValue));
}

////////////////////////////////////////////////////////////////////////////////////////////////////


void JavascriptInterop::Enumerator(const PropertyCallbackInfo<Array>& iInfo)
{
	Handle<External> external = Handle<External>::Cast(iInfo.Holder()->GetInternalField(0));

	JavascriptExternal* wrapper = (JavascriptExternal*)external->Value();

	System::Object^ self = wrapper->GetObject();
	System::Type^ type = self->GetType();

	cli::array<PropertyInfo^>^ members = type->GetProperties(System::Reflection::BindingFlags::Public | System::Reflection::BindingFlags::Instance);
	
	v8::Isolate* isolate = v8::Isolate::GetCurrent();
	EscapableHandleScope handle_scope(isolate);
	Local<Array> result_names = Array::New(isolate, members->Length);
	
	for (int i = 0; i < members->Length; i++)
	{
        PropertyInfo^ member = members[i];
        result_names->Set(i, JavascriptInterop::ConvertToV8(member->Name));
	}

	iInfo.GetReturnValue().Set<Array>(handle_scope.Escape(result_names));
}

////////////////////////////////////////////////////////////////////////////////////////////////////
void
JavascriptInterop::IndexGetter(uint32_t iIndex, const PropertyCallbackInfo<Value> &iInfo)
{
	Handle<External> external = Handle<External>::Cast(iInfo.Holder()->GetInternalField(0));
	JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();
	Handle<Value> value;

	// get property
	value = wrapper->GetProperty(iIndex);
	if (!value.IsEmpty()) {
		iInfo.GetReturnValue().Set(value);
		return;
	}

	// member not found
	iInfo.GetReturnValue().Set(Handle<Value>());
}

////////////////////////////////////////////////////////////////////////////////////////////////////

void
JavascriptInterop::IndexSetter(uint32_t iIndex, Local<Value> iValue, const PropertyCallbackInfo<Value> &iInfo)
{
	Handle<External> external = Handle<External>::Cast(iInfo.Holder()->GetInternalField(0));
	JavascriptExternal* wrapper = (JavascriptExternal*) external->Value();
	Handle<Value> value;

	// get property
	value = wrapper->SetProperty(iIndex, iValue);
	if (!value.IsEmpty()) {
		iInfo.GetReturnValue().Set(value);
		return;
	}

	// member not found
	iInfo.GetReturnValue().Set(Handle<Value>());
}

////////////////////////////////////////////////////////////////////////////////////////////////////

int CountMaximumNumberOfParameters(cli::array<System::Reflection::MemberInfo^>^ members)
{
    int maxParameters = 0;
    for (int i = 0; i < members->Length; i++)
    {
        System::Reflection::MethodInfo^ method = (System::Reflection::MethodInfo^) members[i];
        maxParameters = System::Math::Max(maxParameters, method->GetParameters()->Length);
    }
    return maxParameters;
}

void
JavascriptInterop::Invoker(const v8::FunctionCallbackInfo<Value>& iArgs)
{
	v8::Isolate *isolate = JavascriptContext::GetCurrentIsolate();
	System::Object^ data = UnwrapObject(Handle<External>::Cast(iArgs.Data()));
	System::Reflection::MethodInfo^ bestMethod;
	cli::array<System::Object^>^ suppliedArguments;
	cli::array<System::Object^>^ bestMethodArguments;
	cli::array<System::Object^>^ objectInfo;
	int bestMethodMatchedArgs = -1;
	System::Object^ ret;

	// get target and member's name
	objectInfo = safe_cast<cli::array<System::Object^>^>(data);
	System::Object^ self = objectInfo[0];
	// System::Object^ holder = UnwrapObject(iArgs.Holder());
	System::Type^ holderType = self->GetType(); 
	
	// get members
	System::Type^ type = self->GetType();
	System::String^ memberName = (System::String^)objectInfo[1];
	cli::array<System::Reflection::MemberInfo^>^ members = type->GetMember(memberName);

	if (members->Length > 0 && members[0]->MemberType == System::Reflection::MemberTypes::Method)
	{
        int maxParameters = CountMaximumNumberOfParameters(members);

		// parameters
		suppliedArguments = gcnew cli::array<System::Object^>(maxParameters);
		ConvertedObjects already_converted;
		for (int i = 0; i < maxParameters; i++)
			suppliedArguments[i] = ConvertFromV8(iArgs[i], already_converted);
		
		// look for best matching method
		for (int i = 0; i < members->Length; i++)
		{
			System::Reflection::MethodInfo^ method = (System::Reflection::MethodInfo^) members[i];
			cli::array<System::Reflection::ParameterInfo^>^ parametersInfo = method->GetParameters();
			cli::array<System::Object^>^ arguments;

			// Match arguments & parameters counts.  We will add nulls where
            // we have insufficient parameters.  Note that this checking does
            // not detect where nulls have been supplied (or insufficient parameters
            // have been supplied), but the corresponding parameter cannot accept
            // a null.  This will trigger an exception during invocation.
			if (suppliedArguments->Length <= parametersInfo->Length)
			{
				int match = 0;
				int failed = 0;

				// match parameters
				arguments = gcnew cli::array<System::Object^>(parametersInfo->Length);  // trailing parameters will be null
				for (int p = 0; p < suppliedArguments->Length; p++)
				{
                    System::Reflection::ParameterInfo^ parameter = parametersInfo[p];
					System::Type^ paramType = parameter->ParameterType;

					if (suppliedArguments[p] != nullptr)
					{
						System::Type^ suppliedType = suppliedArguments[p]->GetType();

						if (suppliedType == paramType)
						{
							arguments[p] = suppliedArguments[p];
							match++;
						}
						else
						{
							arguments[p] = SystemInterop::ConvertToType(suppliedArguments[p], paramType);
							if (arguments[p] == nullptr)
							{
								failed++;
								break;
							}
						}
					}
                    else if (parameter->IsOptional && parameter->HasDefaultValue && iArgs[p]->IsUndefined())
                    {
                        // pass default value if parameter is optional and undefined was supplied as an argument
                        arguments[p] = parameter->DefaultValue;
                    }
				}
                for (int p = suppliedArguments->Length; p < arguments->Length; p++)
                {
                    // pass default values if there are optional parameters
                    System::Reflection::ParameterInfo^ parameter = parametersInfo[p];
                    if (parameter->IsOptional && parameter->HasDefaultValue)
                        arguments[p] = parameter->DefaultValue;
                }

				// skip if a conversion failed
				if (failed > 0)
					continue;

				// remember best match
				if (match > bestMethodMatchedArgs)
				{
					bestMethod = method;
					bestMethodArguments = arguments;
					bestMethodMatchedArgs = match;
				}
				else if (match == bestMethodMatchedArgs)
				{
					if (suppliedArguments->Length == parametersInfo->Length) // Prefer method with the most matches and the same length of arguments
					{
						bestMethod = method;
						bestMethodArguments = arguments;
						bestMethodMatchedArgs = match;
					}
				}
				
				/*
					THE CODE BELOW MAY CHOOSE A METHOD PREMATURLY
					for example:
					
					public void test(string a, int b, bool c) { ... }
					public void test(string a, int b, bool c, float d) { ... }
					
					and then invoke it this way in JavaScript:
					
					test("some text", 1234, true, 3.14);
					
					it'll invoke the first one instead of the second because it found 3 matches and there are 3 arguments.
				*/
				// skip lookup if all args matched
				//if (match == arguments->Length)
					//break;
			}
		}
	}

	if (bestMethod != nullptr)
	{
		try
		{
			// invoke
			ret = bestMethod->Invoke(self, bestMethodArguments);
		}
		catch(System::Reflection::TargetInvocationException^ exception)
		{
			iArgs.GetReturnValue().Set(HandleTargetInvocationException(exception));
			return;
		}
		catch(System::Exception^ exception)
		{
			iArgs.GetReturnValue().Set(isolate->ThrowException(JavascriptInterop::ConvertToV8(exception)));
			return;
		}
	}
	else {
		iArgs.GetReturnValue().Set(isolate->ThrowException(JavascriptInterop::ConvertToV8("Argument mismatch for method \"" + memberName + "\".")));
		return;
	}
	
	// return value
	iArgs.GetReturnValue().Set(ConvertToV8(ret));
}


////////////////////////////////////////////////////////////////////////////////////////////////////

Handle<Value>
JavascriptInterop::HandleTargetInvocationException(System::Reflection::TargetInvocationException^ exception)
{
    if (JavascriptContext::GetCurrent()->IsExecutionTerminating())
        // As per comment in V8::TerminateExecution() we should just
        // return here, to allow v8 to keep unwinding its stack.
        // That is, TerminateExecution terminates the whole stack,
        // not just until we notice it in C++ land.
        return Handle<Value>();
    else
	    return JavascriptContext::GetCurrentIsolate()->ThrowException(JavascriptInterop::ConvertToV8(exception->InnerException));
}

////////////////////////////////////////////////////////////////////////////////////////////////////

} } // namespace Noesis::Javascript

////////////////////////////////////////////////////////////////////////////////////////////////////