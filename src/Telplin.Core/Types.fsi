namespace Telplin.Core

type GenericParameter =
    {
        ParameterName : string
        IsHeadType : bool
        IsCompilerGenerated : bool
        Constraints : GenericConstraint list
    }

and GenericConstraint =
    {
        IsEqualityConstraint : bool
        IsComparisonConstraint : bool
        IsReferenceTypeConstraint : bool
        IsSupportsNullConstraint : bool
        CoercesToTarget : string option
        MemberConstraint : MemberConstraintData option
    }

and MemberConstraintData =
    {
        IsStatic : bool
        MemberName : string
        Type : string
    }

type BindingInfo =
    {
        /// The return type of a FSharpMemberOrFunctionOrValue
        ReturnTypeString : string
        /// Generic parameters found in the FSharpMemberOrFunctionOrValue
        BindingGenericParameters : GenericParameter list
        /// Generic parameters found in the (optional) DeclaringEntity (FSharpEntity option)
        TypeGenericParameters : GenericParameter list
    }

type TypeInfoResponse =
    {
        NeedsClassAttribute : bool
        ConstructorInfo : BindingInfo option
    }

type TypedTreeInfoResolver =
    abstract member GetTypeInfo : range : FSharp.Compiler.Text.range -> Result<TypeInfoResponse, string>
    abstract member GetFullForBinding : bindingNameRange : FSharp.Compiler.Text.range -> Result<BindingInfo, string>
    abstract member GetTypeTyparNames : range : FSharp.Compiler.Text.range -> Result<string list, string>

    abstract member GetPropertyWithIndex :
        identifier : string -> range : FSharp.Compiler.Text.range -> Result<BindingInfo, string>

    abstract member Defines : string list
    abstract member IncludePrivateBindings : bool

/// Indicates something went wrong while accessing the Typed tree
type TelplinError = | TelplinError of range : Fantomas.FCS.Text.range * message : string
