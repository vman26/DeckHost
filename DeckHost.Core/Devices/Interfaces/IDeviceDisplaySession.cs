using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeckHost.Core.Devices.Interfaces
{
    public interface IDeviceDisplaySession
    {
        public void RenderAndPush(IDeckAction action, ActionContext actionContext);
    }
}
