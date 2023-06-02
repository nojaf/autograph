﻿namespace Telplin.Core

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
        ReturnTypeString : string
        BindingGenericParameters : GenericParameter list
        TypeGenericParameters : GenericParameter list
    }

type TypeInfoResponse =
    {
        NeedsClassAttribute : bool
        ConstructorInfo : BindingInfo option
    }

type TypedTreeInfoResolver =
    abstract member GetTypeInfo : range : FSharp.Compiler.Text.range -> TypeInfoResponse
    abstract member GetFullForBinding : bindingNameRange : FSharp.Compiler.Text.range -> BindingInfo
    abstract member GetTypeTyparNames : range : FSharp.Compiler.Text.range -> string list
    abstract member GetPropertyWithIndex : identifier : string -> range : FSharp.Compiler.Text.range -> BindingInfo
    abstract member Defines : string list
    abstract member IncludePrivateBindings : bool
