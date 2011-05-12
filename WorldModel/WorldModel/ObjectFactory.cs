﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AxeSoftware.Quest
{
    public interface IElementFactory
    {
        event EventHandler<ObjectsUpdatedEventArgs> ObjectsUpdated;
        ElementType CreateElementType { get; }
        Element Create(string name, bool addToUndoLog);
        Element Create(string name);
        Element Create();
        void DestroyElementSilent(string elementName);
        WorldModel WorldModel { set; }
    }

    public abstract class ElementFactoryBase : IElementFactory
    {
        public event EventHandler<ObjectsUpdatedEventArgs> ObjectsUpdated;

        public abstract ElementType CreateElementType { get; }

        public virtual Element Create(string name)
        {
            return Create(name, true);
        }

        public virtual Element Create(string name, bool addToUndoLog)
        {
            return CreateInternal(name, addToUndoLog, null);
        }

        protected Element CreateInternal(string name, bool addToUndoLog, Action<Element> extraInitialisation)
        {
            if (addToUndoLog)
            {
                WorldModel.UndoLogger.AddUndoAction(new CreateDestroyLogEntry(name, true, null, CreateElementType));
            }

            Element newElement = new Element(WorldModel);

            try
            {
                WorldModel.Elements.Add(CreateElementType, name, newElement);
            }
            catch (ArgumentException e)
            {
                throw new Exception(string.Format("Cannot add {0} '{1}': {2}", FriendlyElementTypeName, name, e.Message), e);
            }

            newElement.Name = name;
            newElement.ElemType = CreateElementType;

            if (extraInitialisation != null)
            {
                extraInitialisation.Invoke(newElement);
            }

            NotifyAddedElement(name);

            return newElement;
        }

        public virtual Element Create()
        {
            string id = WorldModel.GetUniqueID();
            return Create(id);
        }

        protected string FriendlyElementTypeName
        {
            get
            {
                return ((ElementTypeInfo)(typeof(ElementType).GetField(CreateElementType.ToString()).GetCustomAttributes(typeof(ElementTypeInfo), false)[0])).Name;
            }
        }

        public WorldModel WorldModel { get; set; }

        protected void NotifyAddedElement(string elementName)
        {
            if (ObjectsUpdated != null) ObjectsUpdated(this, new ObjectsUpdatedEventArgs { Added = elementName });
        }

        protected void NotifyRemovedElement(string elementName)
        {
            if (ObjectsUpdated != null) ObjectsUpdated(this, new ObjectsUpdatedEventArgs { Removed = elementName });
        }

        public void DestroyElement(string elementName)
        {
            DestroyElement(elementName, false);
        }

        public void DestroyElementSilent(string elementName)
        {
            DestroyElement(elementName, true);
        }

        private void DestroyElement(string elementName, bool silent)
        {
            try
            {
                Element destroy = WorldModel.Elements.Get(elementName);
                if (!silent) AddDestroyToUndoLog(destroy, destroy.Type);
                NotifyRemovedElement(elementName);
                WorldModel.RemoveElement(CreateElementType, elementName);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Cannot destroy element '{0}': {1}", elementName, e.Message), e);
            }
        }

        private void AddDestroyToUndoLog(Element appliesTo, ObjectType type)
        {
            Fields fields = appliesTo.Fields;

            Dictionary<string, object> allAttributes = fields.GetAllAttributes();

            foreach (string attr in allAttributes.Keys)
            {
                WorldModel.UndoLogger.AddUndoAction(new UndoFieldSet(WorldModel, appliesTo.Name, attr, allAttributes[attr], null, false));
            }

            WorldModel.UndoLogger.AddUndoAction(new CreateDestroyLogEntry(appliesTo.Name, false, type, CreateElementType));
        }

        protected class CreateDestroyLogEntry : AxeSoftware.Quest.UndoLogger.IUndoAction
        {
            private bool m_create;
            private ObjectType? m_type;
            private string m_name;
            private ElementType m_elemType;

            public CreateDestroyLogEntry(string name, bool create, ObjectType? type, ElementType elemType)
            {
                m_create = create;
                m_name = name;
                m_type = type;
                m_elemType = elemType;
            }

            private void CreateElement(WorldModel worldModel)
            {
                if (m_elemType == ElementType.Object)
                {
                    worldModel.ObjectFactory.CreateObject(m_name, m_type.Value, false);
                }
                else
                {
                    worldModel.GetElementFactory(m_elemType).Create(m_name, false);
                }
            }

            private void DestroyElement(WorldModel worldModel)
            {
                worldModel.GetElementFactory(m_elemType).DestroyElementSilent(m_name);
            }

            public void DoUndo(WorldModel worldModel)
            {
                if (m_create)
                {
                    DestroyElement(worldModel);
                }
                else
                {
                    CreateElement(worldModel);
                }
            }

            public void DoRedo(WorldModel worldModel)
            {
                if (m_create)
                {
                    CreateElement(worldModel);
                }
                else
                {
                    DestroyElement(worldModel);
                }
            }
        }
    }

    public class ObjectFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType { get { return ElementType.Object; } }

        public override Element Create(string name)
        {
            return CreateObject(name);
        }

        public Element CreateObject(string objectName)
        {
            return CreateObject(objectName, ObjectType.Object);
        }

        public Element CreateObject(string objectName, ObjectType type)
        {
            return CreateObject(objectName, type, true);
        }

        public Element CreateObject(ObjectType type)
        {
            if (type == ObjectType.Exit)
            {
                return CreateObject(WorldModel.GetUniqueID("exit"), type);
            }
            else
            {
                return CreateObject(WorldModel.GetUniqueID(), type);
            }
        }

        internal Element CreateObject(string objectName, ObjectType type, bool addToUndoLog)
        {
            WorldModel.UndoLogger.AddUndoAction(new CreateDestroyLogEntry(objectName, true, type, ElementType.Object));
            Element newObject = base.CreateInternal(objectName, false, newElement => newElement.Type = type);

            string defaultType = WorldModel.DefaultTypeNames[type];

            if (WorldModel.State == GameState.Running)
            {
                if (WorldModel.Elements.ContainsKey(ElementType.ObjectType, defaultType))
                {
                    newObject.AddType(WorldModel.GetObjectType(defaultType));
                }
            }
            else
            {
                newObject.Fields.LazyFields.AddDefaultType(defaultType);
            }

            return newObject;
        }

        public Element CreateObject(string objectName, Element parent)
        {
            Element newObject = CreateObject(objectName);
            newObject.Parent = parent;
            return newObject;
        }

        public Element CreateCommand()
        {
            string id = WorldModel.GetUniqueID();
            return CreateCommand(id);
        }

        public Element CreateCommand(string id)
        {
            Element newCommand = CreateObject(id, ObjectType.Command);
            newCommand.Type = ObjectType.Command;
            return newCommand;
        }

        public Element CreateExit(string exitName, Element fromRoom, Element toRoom)
        {
            string exitID = WorldModel.GetUniqueID("exit");
            if (WorldModel.ObjectExists(exitID)) exitID = WorldModel.GetUniqueID(exitID);
            Element newExit = CreateExit(exitID, exitName, fromRoom, toRoom);
            newExit.Fields[FieldDefinitions.Anonymous] = true;
            return newExit;
        }

        public Element CreateExit(string exitID, string exitName, Element fromRoom, Element toRoom)
        {
            Element newExit = CreateObject(exitID, ObjectType.Exit);
            newExit.Fields[FieldDefinitions.Alias] = exitName;
            newExit.Parent = fromRoom;
            newExit.Fields[FieldDefinitions.To] = toRoom;
            newExit.Type = ObjectType.Exit;
            return newExit;
        }

        public Element CreateExitLazy(string exitName, Element fromRoom, string toRoom)
        {
            Element newExit = CreateExit(exitName, fromRoom, null);
            InitLazyExit(newExit, toRoom);
            return newExit;
        }

        public Element CreateExitLazy(string exitID, string exitName, Element fromRoom, string toRoom)
        {
            Element newExit = CreateExit(exitID, exitName, fromRoom, null);
            InitLazyExit(newExit, toRoom);
            return newExit;
        }

        private void InitLazyExit(Element exit, string toRoom)
        {
            if (toRoom != null)
            {
                exit.Fields.LazyFields.AddObjectField("to", toRoom);
            }
        }
    }

    internal class ObjectTypeFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.ObjectType; }
        }

        public override Element Create(string name)
        {
            Element newType = base.Create(name);
            newType.Fields.MutableFieldsLocked = true;
            return newType;
        }
    }

    internal class EditorFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Editor; }
        }
    }

    internal class EditorTabFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.EditorTab; }
        }
    }

    internal class EditorControlFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.EditorControl; }
        }
    }

    internal class FunctionFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Function; }
        }
    }

    internal class DelegateFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Delegate; }
        }
    }

    internal class TemplateFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Template; }
        }
    }

    internal class DynamicTemplateFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.DynamicTemplate; }
        }
    }

    internal abstract class SingleElementFactory : ElementFactoryBase
    {
        public override Element Create(string name)
        {
            if (WorldModel.Elements.Count(CreateElementType) > 1)
            {
                throw new InvalidOperationException(string.Format("There can only be one '{0}' element", FriendlyElementTypeName));
            }
            return base.Create(name);
        }
    }

    internal class WalkthroughFactory : SingleElementFactory
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Walkthrough; }
        }
    }

    internal class IncludedLibraryFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.IncludedLibrary; }
        }
    }

    internal class ImpliedTypeFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.ImpliedType; }
        }
    }

    internal class JavascriptReferenceFactory : ElementFactoryBase
    {
        public override ElementType CreateElementType
        {
            get { return ElementType.Javascript; }
        }
    }
}
