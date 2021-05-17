using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Noesis.Javascript.Tests
{
    [TestClass]
    public class AccessorInterceptorTests
    {
        private JavascriptContext _context;

        [TestInitialize]
        public void SetUp()
        {
            _context = new JavascriptContext();
        }

        [TestCleanup]
        public void TearDown()
        {
            _context.Dispose();
        }

        [TestMethod]
        public void AccessAnElementInAManagedArray()
        {
            int[] myArray = new int[] { 151515, 666, 2555, 888, 99 };
            _context.SetParameter("myArray", myArray);

           _context.Run("myArray[2] == 2555").Should().BeOfType<bool>().Which.Should().BeTrue();
        }

        class ClassWithIndexer
        {
            public int Index { get; set; }
            public string Value { get; set; }

            public string this[int iIndex]
            {
                get { return (Value + " " + iIndex); }
                set { 
                    Value = value;
                    Index = iIndex;
                }
            }
        }

        [TestMethod]
        public void ClassWithIndexer_IndexerIsIgnoredDuringEnumeration()
        {
            _context.SetParameter("myObject", new ClassWithIndexer { Index = 1, Value = "asdf" });
            _context.Run("JSON.stringify(myObject)").Should().Be("{\"Index\":1,\"Value\":\"asdf\"}");
        }

        [TestMethod]
        public void AccessingByIndexAPropertyInAManagedObject()
        {
            _context.SetParameter("myObject", new ClassWithIndexer { Value = "Value"});

            _context.Run("myObject[99] == 'Value 99'").Should().BeOfType<bool>().Which.Should().BeTrue();
        }

        class ClassWithToJSONMethod
		{
			public string Test { get; set; }

            public object ToJSON()
			{
				return new int[] { 1, 2, 3, 4 };
			}
		}

		[TestMethod]
		public void ToJSONMethodIsUsedIfAvailable()
		{
			_context.SetParameter("myObject", new ClassWithToJSONMethod { Test = "asdf" });
			_context.Run("JSON.stringify(myObject)").Should().Be("[1,2,3,4]");
		}

		class ClassWithDictionary
        {
            public DictionaryLike prop { get; set; }
        }

		class DictionaryLike
		{
			public Dictionary<string, object> internalDict { get; set; }

			public DictionaryLike(Dictionary<string, object> internalDict = null)
			{
				this.internalDict = internalDict ?? new Dictionary<string, object>();
			}

            public bool Contains(string key) => internalDict.ContainsKey(key);
            public void Remove(string key) => internalDict.Remove(key);
			
			public object this[string key]
			{
				get => internalDict[key];
				set => internalDict[key] = value;
			}

            public object ToJSON()
            {
                return internalDict;
            }

            [ObjectKeys]
            public string[] GetKeys() => internalDict.Keys.ToArray();
        }

        enum PersonType
        {
            A,
            B
        }

        class Person
        {
            // in order of conversion methods in JavascriptInterop::ConvertToV8
            // except for the DateTime property, because it is more likely to get a crash if it is at the end
            
            //public System.Numerics.BigInteger BigInt { get; set; } // can't be serialized
            public PersonType PersonType { get; set; }
            public char Char { get; set; }
            public string Name { get; set; }
            public Regex Regex { get; set; }
            public Func<int> Callback { get; set; }
            public Dictionary<string, object> Dict { get; set; }
            public List<string> List { get; set; }
            public Exception Exception { get; set; }
            public DateTime Birthdate { get; set; }
            public Address Address { get; set; }
        }

        class Address
        {
            public string City { get; set; }
            public Person Person { get; set; }
        }

        [TestMethod]
        public void StringifyOfRecursiveObject_ShouldThrowAndNotCrash()
        {
            // When calling JSON.stringify on a recursive data structure that contains a DateTime object we experienced a crash 
            // because of the call to ToLocalChecked in the return statement of JavascriptInterop::ConvertDateTimeToV8. This test
            // case simulates that for any type that has a call to ToLocalChecked that can be reached from JavascriptInterop::ConvertToV8.
            // The crash happened because ToLocalChecked sends us to FatalErrorCallback (JavascriptContext.cpp) which then terminates the process.
            // However, there is already an exception pending when we reach that ToLocalChecked call because the maximum call stack size
            // has been exceeded while stringifying. This is the actual reason why we get an empty MaybeLocal in the first place which
            // then leads to the crash. Interestingly, this *only* seems to happen for the case of the DateTime conversion.
            var address = new Address { City = "asdf" };
            var person = new Person
            {
                PersonType = PersonType.A,
                Char = 'a',
                Birthdate = DateTime.Today,
                Name = "test",
                Regex = new Regex("asdf", RegexOptions.ECMAScript),
                Callback = () => 42,
                Dict = new Dictionary<string, object> { { "a", 42 } },
                List = new List<string> { "qwer" },
                Exception = new Exception("foo"),
                Address = address
            };
            address.Person = person;
            _context.SetParameter("person", person);
            //Assert.AreEqual("", _context.Run("JSON.stringify(person)")); // comment Address in Person and uncomment this to check the general shape of the JSON
            Action action = () => _context.Run("JSON.stringify(person)");
            action.ShouldThrowExactly<JavascriptException>().Which.Message.Should().StartWith("TypeError: Converting circular structure to JSON");
        }

        [TestMethod]
        public void DictionaryCanBeStringified()
        {
            _context.SetParameter("myObject", new ClassWithDictionary { prop = new DictionaryLike(new Dictionary<string, object> { { "test", 42 } }) });
            _context.Run("JSON.stringify(myObject)").Should().Be("{\"prop\":{\"test\":42}}");
        }

        [TestMethod]
        public void DictionaryCanBeStringifiedMultipleTimes()
        {
            _context.SetParameter("myObject", new ClassWithDictionary { prop = new DictionaryLike(new Dictionary<string, object> { { "test", 42 } }) });
            _context.SetParameter("myObject2", new ClassWithDictionary { prop = new DictionaryLike(new Dictionary<string, object> { { "test", 23 } }) });
            _context.Run("JSON.stringify(myObject)").Should().Be("{\"prop\":{\"test\":42}}");
            _context.Run("JSON.stringify(myObject2)").Should().Be("{\"prop\":{\"test\":23}}");
        }

        [TestMethod]
        public void ObjectKeys()
        {
            var iObject = new ClassWithDictionary { prop = new DictionaryLike(new Dictionary<string, object> { { "test", 42 } }) };
            _context.SetParameter("myObject", iObject);
            var result = _context.Run("Object.keys(myObject.prop)");
            result.Should().BeOfType<object[]>().Which.Should().HaveCount(1);
            ((object[]) result)[0].Should().Be("test");
        }

        [TestMethod]
        public void ObjectGetOwnPropertyNames()
        {
            var iObject = new ClassWithDictionary { prop = new DictionaryLike(new Dictionary<string, object> { { "test", 42 } }) };
            _context.SetParameter("myObject", iObject);
            var result = _context.Run("Object.getOwnPropertyNames(myObject.prop)");
            result.Should().BeOfType<object[]>().Which.Should().HaveCount(1);
            ((object[]) result)[0].Should().Be("test");
        }

        [TestMethod]
        public void AccessingDictionaryInManagedObject()
        {
            var dict = new Dictionary<string, object> { { "bar", "33" }, { "baz", true } };
            ClassWithDictionary testObj = new ClassWithDictionary() { prop = new DictionaryLike(dict) };

            _context.SetParameter("test", testObj);
            var result = _context.Run(@"test.prop.foo = 42;
test.prop.baz = false;
var complex = {};
complex.v0 = test.prop.foo
test.prop.complex = complex;");

            testObj.prop.internalDict.Count.Should().Be(4);
            testObj.prop.internalDict["foo"].Should().Be(42);
            testObj.prop.internalDict["bar"].Should().Be("33");
            testObj.prop.internalDict["baz"].Should().Be(false);
			testObj.prop.internalDict["complex"].Should().BeOfType<Dictionary<string, object>>();
			((Dictionary<string,object>)testObj.prop.internalDict["complex"])["v0"].Should().Be(42);
		}
		
		[TestMethod]
		public void AccessingDictionaryDirectlyInManagedObject()
		{
			var dict = new Dictionary<string, object>() { { "bar", "33" }, { "baz", true } };

			_context.SetParameter("test", dict);
			var result = _context.Run(@"test.foo = 42; test.baz = false;");
			var testObjResult = (Dictionary<string, object>)_context.GetParameter("test");

			testObjResult.Count.Should().Be(3);
			testObjResult["foo"].Should().Be(42);
			testObjResult["bar"].Should().Be("33");
			testObjResult["baz"].Should().Be(false);
		}


		[TestMethod]
		public void AccessingDictionaryOverObjectInManagedObject()
		{
			DictionaryLike testObj = new DictionaryLike();
			testObj.internalDict["foo"] = 42;

			_context.SetParameter("test", testObj);
			var result = _context.Run(@"test.foo;");
			
			result.Should().Be(42);
			testObj.internalDict.Count.Should().Be(1);
			testObj.internalDict["foo"].Should().Be(42);
		}

        [TestMethod]
        public void AccessingDictionaryOverObjectInManagedObject_AccessingNonExistentKeyReturnsUndefined()
        {
            DictionaryLike testObj = new DictionaryLike();

            _context.SetParameter("test", testObj);
            var result = _context.Run(@"test.foo === undefined;");

            result.Should().Be(true);
        }

        [TestMethod]
        public void AccessingDictionaryOverObjectInManagedObject_AccessingANullValueReturnsNull()
        {
            DictionaryLike testObj = new DictionaryLike();
            testObj.internalDict["foo"] = null;

            _context.SetParameter("test", testObj);
            var result = _context.Run(@"test.foo === null;");

            result.Should().Be(true);
        }

        [TestMethod]
        public void AccessingDictionaryOverObjectInManagedObject_SettingAnEntryToUndefinedDeletesTheEntryFromTheDictionary()
        {
            DictionaryLike testObj = new DictionaryLike();
            testObj.internalDict["foo"] = 42;

            testObj.internalDict.Count.Should().Be(1);

            _context.SetParameter("test", testObj);
            var result = _context.Run(@"test.foo = undefined;");

            testObj.internalDict.Count.Should().Be(0);
        }

        [TestMethod]
		public void AccessingDictionaryOverObjectInManagedObject2()
		{
			DictionaryLike testObj = new DictionaryLike();
			
			_context.SetParameter("test", testObj);
			var result = _context.Run(@"test.foo = 'bar';");
			
			testObj.internalDict.Count.Should().Be(1);
			testObj.internalDict["foo"].Should().Be("bar");
		}

        class ClassWithProperty
        {
            public string MyProperty { get; set; }

            [DoNotEnumerate]
            public string MyPropertyInternal { get; set; }
        }

        [TestMethod]
        public void ToJSONPrivateProperty()
        {
            _context.SetParameter("myObject", new ClassWithProperty { MyProperty = "asdf", MyPropertyInternal = "qwer" });
            _context.Run("JSON.stringify(myObject)").Should().Be("{\"MyProperty\":\"asdf\"}");
        }

        [TestMethod]
        public void AccessingByNameAPropertyInManagedObject()
        {
            _context.SetParameter("myObject", new ClassWithProperty { MyProperty = "This is the string return by \"MyProperty\"" });

            _context.Run("myObject.MyProperty == 'This is the string return by \"MyProperty\"'").Should().BeOfType<bool>().Which.Should().BeTrue();
        }

		[TestMethod]
		public void ObjectKeys_NoPropertyEnum_EmptyArray()
		{
			_context.SetParameter("obj", new { });

			var result = _context.Run("Object.keys(obj);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(0);
		}

		[TestMethod]
		public void ObjectKeys_SinglePropertyEnum_OnePropertyName()
		{
			_context.SetParameter("myObject", new ClassWithProperty { MyProperty = "" });

			var result = _context.Run("Object.keys(myObject);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(1);
			((object[]) result)[0].Should().Be("MyProperty");
		}


		[TestMethod]
		public void ObjectKeys_MDN_StringObj_ThreePropertyNames()
		{
			//> Object.keys("foo") => TypeError: "foo" is not an object		// ES5 code
			//> Object.keys("foo") => ["0", "1", "2"]						// ES2015 code
			_context.SetParameter("obj", "foo");

			var result = _context.Run("Object.keys(obj);");
			
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(3);
			((object[])result)[0].Should().Be("0");
			((object[])result)[1].Should().Be("1");
			((object[])result)[2].Should().Be("2");
		}

		[TestMethod]
		public void ObjectKeys_MDN_ArrayThreePropertiesEnum_ThreePropertyNames()
		{
			//var arr = ['a', 'b', 'c'];
			//console.log(Object.keys(arr)); // console: ['0', '1', '2']
			_context.SetParameter("arr", new string[]{ "a","b","c" });
			
			var result = _context.Run("Object.keys(arr);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(3);
			((object[])result)[0].Should().Be("0");
			((object[])result)[1].Should().Be("1");
			((object[])result)[2].Should().Be("2");
		}
		
		class ClassWithFunctionProperty
		{
			public void TestFunc() { }
		}

		[TestMethod]
		public void ObjectKeys_FunctionAsProperty_EmptyArray()
		{
			_context.SetParameter("myObject", new ClassWithFunctionProperty() );

			var result = _context.Run("Object.keys(myObject);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(0);
		}
		
		[TestMethod]
		public void ObjectKeys_FunctionAsParam_EmptyArray()
		{
			Func<double> fooFunc = () => 42;
			_context.SetParameter("myObject", fooFunc);

			var result = _context.Run("Object.keys(myObject);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(0);
		}
		
		[TestMethod]
		public void ForInLoop_ObjectWithoutProperties_EmptyArray()
		{
			_context.SetParameter("myObject", new {  });

			var result = _context.Run(@"var result = [];
for(var prop in myObject) {
result.push(prop);
}
result;");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(0);
		}

		[TestMethod]
		public void ForInLoop_ObjectProperties_OnePropertyName()
		{
			_context.SetParameter("myObject", new ClassWithProperty() { });

			var result = _context.Run(@"var result = [];
for(var prop in myObject) {
result.push(prop);
}
result;");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(1);
			((object[])result)[0].Should().Be("MyProperty");
		}

		abstract class ClassAsSuperClass
		{
			public string MySuperClassProperty { get; set; }
		}

		class ClassAsSubClass : ClassAsSuperClass
		{
			public string MySubClassProperty { get; set; }
		}

		[TestMethod]
		public void ObjectKeys_OnlyOwnPropertiesInInheritance_OneName()
		{
			_context.SetParameter("myObject", new ClassAsSubClass());
	
			var result = _context.Run("Object.keys(myObject);");
			result.Should().BeOfType<object[]>().Which.Should().HaveCount(2);
			((object[])result)[0].Should().Be("MySubClassProperty");
		}


		[TestMethod]
		public void ForInLoop_ObjectInheritanceProperties_OnlySubClassPropertyName()
		{
			_context.SetParameter("myObject", new { MyProperty = "" });

			var result = _context.Run(@"var result = [];
//for(var prop in myObject) {
//result.push(prop);
//}
//result;");
		}


		class ClassWithDecimalProperty
        {
            public decimal D { get; set; }
        }

        [TestMethod]
        public void AccessingByNameADecimalPropertyInManagedObject()
        {
            var myObject = new ClassWithDecimalProperty { D = 42 };
            _context.SetParameter("myObject", myObject);

            _context.Run("myObject.D = 43; myObject.D").Should().BeOfType<int>().Which.Should().Be(43);
            myObject.D.Should().Be(43);
        }

        [TestMethod]
        public void GracefullyHandlesAttemptsToAccessByIndexerWhenIndexerDoesntExist()
        {
            _context.SetParameter("myObject", new ClassWithProperty());

            _context.Run("myObject[20] === undefined").Should().BeOfType<bool>().Which.Should().BeTrue(); ;
        }

        [TestMethod]
        public void SetValueByIndexerInManagedObject()
        {
            var classWithIndexer = new ClassWithIndexer();
            _context.SetParameter("myObject", classWithIndexer);

            _context.Run("myObject[20] = 'The value is now set'");

            classWithIndexer.Value.Should().Be("The value is now set");
            classWithIndexer.Index.Should().Be(20);
        }

        [TestMethod]
        public void SetPropertyByNameInManagedObject()
        {
            var classWithProperty = new ClassWithProperty();
            _context.SetParameter("myObject", classWithProperty);

            _context.Run("myObject.MyProperty = 'hello'");

            classWithProperty.MyProperty.Should().Be("hello");
        }

        [TestMethod]
        public void SettingUnknownPropertiesIsAllowed()
        {
            _context.SetParameter("myObject", new ClassWithProperty());

            _context.Run("myObject.UnknownProperty = 77");

            _context.Run("myObject.UnknownProperty").Should().Be(77);
        }

        [TestMethod]
        public void SettingUnknownPropertiesIsDisallowedIfRejectUnknownPropertiesIsSet()
        {
            _context.SetParameter("myObject", new ClassWithProperty(), SetParameterOptions.RejectUnknownProperties);

            Action action = () => _context.Run("myObject.UnknownProperty = 77");
            action.ShouldThrowExactly<JavascriptException>();
        }
        
        [TestMethod]
        public void GettingUnknownPropertiesIsDisallowedIfRejectUnknownPropertiesIsSet()
        {
            _context.SetParameter("myObject", new ClassWithProperty(), SetParameterOptions.RejectUnknownProperties);

            Action action = () => _context.Run("myObject.UnknownProperty");
            action.ShouldThrowExactly<JavascriptException>().Which.Message.Should().StartWith("Unknown member:");
        }

        class ClassForTypeCoercion
        {
            public bool BooleanValue { get; set; }
            public UriKind EnumeratedValue { get; set; }
        }

        [TestMethod]
        public void TypeCoercionToBoolean()
        {
            var my_object = new ClassForTypeCoercion();
            _context.SetParameter("my_object", my_object);
            _context.Run("my_object.BooleanValue = true");
            my_object.BooleanValue.Should().BeTrue();
        }

        [TestMethod]
        public void TypeCoercionStringToEnum()
        {
            var my_object = new ClassForTypeCoercion();
            _context.SetParameter("my_object", my_object);
            _context.Run("my_object.EnumeratedValue = 'Absolute'");
            my_object.EnumeratedValue.Should().Be(UriKind.Absolute);
        }

        [TestMethod]
        public void TypeCoercionNumberToEnum()
        {
            var my_object = new ClassForTypeCoercion();
            _context.SetParameter("my_object", my_object);
            _context.Run("my_object.EnumeratedValue = 1.0");
            my_object.EnumeratedValue.Should().Be(UriKind.Absolute);
        }

        class ClassWithEnumerableProperty
        {
            public ClassWithEnumerableProperty()
            {
                ComplexItems = new HashSet<ClassWithDecimalProperty>
                {
                    new ClassWithDecimalProperty { D = 1 },
                    new ClassWithDecimalProperty { D = 2 },
                    new ClassWithDecimalProperty { D = 3 },
                };
            }

            public IEnumerable<int> Items
            {
                get
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }
            }

            public IEnumerable<int> EmptyItems
            {
                get { return new HashSet<int>(); }
            }

			public IEnumerable<ClassWithDecimalProperty> ComplexItems { get; }
		}

        [TestMethod]
        public void IEnumerableProperty_CallingSymbolIteratorFunction_ReturnsACorrectIterator()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
var iterator = enumerable.Items[Symbol.iterator]();
var next = iterator.next();
next;
");
            result.Should().BeAssignableTo(typeof(Dictionary<string, object>));
            var iteratorResult = (Dictionary<string, object>) result;
            iteratorResult.ContainsKey("done").Should().Be(true);
            iteratorResult.ContainsKey("value").Should().Be(true);
            iteratorResult["done"].Should().Be(false);
            iteratorResult["value"].Should().Be(1);
        }

        [TestMethod]
        public void IEnumerableComplexObjectProperty_CallingSymbolIteratorFunction_ReturnsACorrectIterator()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
var iterator = enumerable.ComplexItems[Symbol.iterator]();
var next = iterator.next();
next;
");
            result.Should().BeAssignableTo(typeof(Dictionary<string, object>));
            var iteratorResult = (Dictionary<string, object>) result;
            iteratorResult.ContainsKey("done").Should().Be(true);
            iteratorResult.ContainsKey("value").Should().Be(true);
            iteratorResult["done"].Should().Be(false);
            iteratorResult["value"].Should().BeAssignableTo(typeof(ClassWithDecimalProperty));
            var value = (ClassWithDecimalProperty) iteratorResult["value"];
            value.D.Should().Be(1);
        }

        [TestMethod]
        public void IEnumerableProperty_IteratingUsingNextFunction_ReturnsCorrectSuccessiveResults()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
function assert(expected, actual) {
    for (let prop in expected) {
        if (!(prop in actual))
            throw new Error(`Property ${prop} not available on ${JSON.stringify(actual)}`);
        if (expected[prop] !== actual[prop])
            throw new Error(`Expected ${prop} to be ${expected[prop]} but was ${actual[prop]}`);
    }
}
var iterator = enumerable.Items[Symbol.iterator]();
var next = iterator.next();
assert({ done: false, value: 1 }, next);
next = iterator.next();
assert({ done: false, value: 2 }, next);
next = iterator.next();
assert({ done: false, value: 3 }, next);
next = iterator.next();
assert({ done: true }, next);
next = iterator.next();
assert({ done: true }, next);
");
        }

        [TestMethod]
        public void IEnumerableProperty_CallingIteratorFunctionAgainAfterIterating_SequenceStartsFromBeginning()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
function assert(expected, actual) {
    for (let prop in expected) {
        if (!(prop in actual))
            throw new Error(`Property ${prop} not available on ${JSON.stringify(actual)}`);
        if (expected[prop] !== actual[prop])
            throw new Error(`Expected ${prop} to be ${expected[prop]} but was ${actual[prop]}`);
    }
}
var iterator = enumerable.Items[Symbol.iterator]();
var next = iterator.next();
assert({ done: false, value: 1 }, next);
next = iterator.next();
assert({ done: false, value: 2 }, next);
next = iterator.next();
assert({ done: false, value: 3 }, next);
next = iterator.next();
assert({ done: true }, next);

iterator = enumerable.Items[Symbol.iterator]();
next = iterator.next();
assert({ done: false, value: 1 }, next);
");
        }

        [TestMethod]
        public void IEnumerableComplexObjectProperty_CallingSymbolIteratorFunctionTwice_SequenceStartsFromBeginning()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
var iterator = enumerable.ComplexItems[Symbol.iterator]();
iterator.next();
iterator.next();

iterator = enumerable.ComplexItems[Symbol.iterator]();
var next = iterator.next();
next;
");
            result.Should().BeAssignableTo(typeof(Dictionary<string, object>));
            var iteratorResult = (Dictionary<string, object>) result;
            iteratorResult.ContainsKey("done").Should().Be(true);
            iteratorResult.ContainsKey("value").Should().Be(true);
            iteratorResult["done"].Should().Be(false);
            iteratorResult["value"].Should().BeAssignableTo(typeof(ClassWithDecimalProperty));
            var value = (ClassWithDecimalProperty) iteratorResult["value"];
            value.D.Should().Be(1);
        }

        [TestMethod]
        public void IEnumerableProperty_IteratingAnEmptyCollection_ReturnsCorrectObjectWithDoneEqualToTrue()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
function assert(expected, actual) {
    for (let prop in expected) {
        if (!(prop in actual))
            throw new Error(`Property ${prop} not available on ${JSON.stringify(actual)}`);
        if (expected[prop] !== actual[prop])
            throw new Error(`Expected ${prop} to be ${expected[prop]} but was ${actual[prop]}`);
    }
}
var iterator = enumerable.EmptyItems[Symbol.iterator]();
var next = iterator.next();
assert({ done: true }, next);
");
        }

        [TestMethod]
        public void IEnumerableProperty_UsingForOfLoopToIterate_ReturnsCorrectResult()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
let result = 0;
for (const item of enumerable.Items)
    result += item;
result;
");
            result.Should().Be(6);
        }

        [TestMethod]
        public void IEnumerableProperty_UsingForOfLoopTwice_ReturnsCorrectResult()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
let result = 0;
for (const item of enumerable.Items)
    result += item;
result = 0;
for (const item of enumerable.Items)
    result += item;
result;
");
            result.Should().Be(6);
        }

        [TestMethod]
        public void IEnumerableProperty_UsingForOfLoopToIterateComplexObjects_ReturnsCorrectResult()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
let result = 0;
for (const item of enumerable.ComplexItems)
    result += item.D;
result;
");
            result.Should().Be(6);
        }

        [TestMethod]
        public void IEnumerableProperty_UsingSpreadOperator_ReturnsCorrectArray()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"[...enumerable.Items];");
            result.ShouldBeEquivalentTo(new int[] { 1, 2, 3 });
        }

        [TestMethod]
        public void IEnumerableComplexObjectProperty_UsingSpreadOperator_ReturnsCorrectArray()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"[...enumerable.ComplexItems]");
            result.ShouldBeEquivalentTo(new ClassWithDecimalProperty[] 
            {
                new ClassWithDecimalProperty { D = 1 },
                new ClassWithDecimalProperty { D = 2 },
                new ClassWithDecimalProperty { D = 3 },
            });
        }

        [TestMethod]
        public void IEnumerableComplexObjectProperty_UsingSpreadOperator_CanUseArrayOperationsCorrectly()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            var result = _context.Run(@"
const array = [...enumerable.ComplexItems];
array.map(x => x.D).join(', ');
");
            result.Should().Be("1, 2, 3");
        }

        [TestMethod]
        public void PropertyThatIsNotIEnumerable_CallingSymbolIteratorFunction_ThrowsExceptionBecauseItIsUndefined()
        {
            var enumerable = new ClassWithEnumerableProperty();
            _context.SetParameter("enumerable", enumerable);
            Action action = () => _context.Run(@"enumerable[Symbol.iterator]()");
            action.ShouldThrow<JavascriptException>("TypeError: enumerable[Symbol.iterator] is not a function");
        }
    }
}