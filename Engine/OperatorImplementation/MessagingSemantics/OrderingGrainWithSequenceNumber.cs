﻿using Orleans;
using Orleans.Streams;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Orleans.Concurrency;
using TexeraUtilities;
using Engine.OperatorImplementation.Common;


namespace Engine.OperatorImplementation.MessagingSemantics
{
    //Ensures FIFO and exactly once delivery between two actors
    public class OrderingGrainWithSequenceNumber : IOrderingEnforcer
    {
        private Dictionary<IGrain,Dictionary<ulong, Pair<bool,List<TexeraTuple>>>> stashedPayloadMessages = new Dictionary<IGrain, Dictionary<ulong, Pair<bool, List<TexeraTuple>>>>();
        public Dictionary<IGrain,ulong> inSequenceNumberMap=new Dictionary<IGrain, ulong>();
        private enum MessageStatus
        {
            Vaild,
            Duplicated,
            Ahead,
        }

        private string self;

        public OrderingGrainWithSequenceNumber(string self)
        {
            this.self = self;
        }

        private MessageStatus CheckMessage(IGrain sender, ulong sequenceNum)
        {
            if(!inSequenceNumberMap.ContainsKey(sender))
            {
                inSequenceNumberMap[sender]=0;
            }  
            ulong currentSequenceNumber=inSequenceNumberMap[sender];
            if(sequenceNum < currentSequenceNumber)
            {
                // de-dup messages
                Console.WriteLine(self+" Received duplicated message from "+Utils.GetReadableName(sender)+" with seqnum = "+sequenceNum+" current seqnum = "+currentSequenceNumber);
                return MessageStatus.Duplicated;
            }
            if (sequenceNum != currentSequenceNumber)
            {
                return MessageStatus.Ahead;           
            }
            return MessageStatus.Vaild;
        }

        public bool PreProcess(PayloadMessage message)
        {
            IGrain sender = message.SenderIdentifer;
            ulong sequenceNum=message.SequenceNumber;
            switch(CheckMessage(sender,sequenceNum))
            {
                case MessageStatus.Vaild:
                    inSequenceNumberMap[sender]++;
                    return true;
                case MessageStatus.Ahead:
                    if(!stashedPayloadMessages.ContainsKey(sender))
                    {
                        stashedPayloadMessages[sender]=new Dictionary<ulong, Pair<bool, List<TexeraTuple>>>();
                    }
                    if(!stashedPayloadMessages[sender].ContainsKey(sequenceNum))
                    {
                        stashedPayloadMessages[sender].Add(sequenceNum, new Pair<bool, List<TexeraTuple>>(message.IsEnd,message.Payload));
                    }
                    break;
                case MessageStatus.Duplicated:
                    break;
            }
            return false;
        }

        public void CheckStashed(ref List<TexeraTuple> batchList, ref bool isEnd, IGrain sender)
        {
            if(stashedPayloadMessages.ContainsKey(sender))
            {
                if(!inSequenceNumberMap.ContainsKey(sender))
                {
                    inSequenceNumberMap[sender]=0;
                }
                ulong currentSequenceNumber=inSequenceNumberMap[sender];
                Dictionary<ulong, Pair<bool,List<TexeraTuple>>> currentMap=stashedPayloadMessages[sender];
                while(currentMap.ContainsKey(currentSequenceNumber))
                {
                    if(batchList==null)
                    {
                        batchList=new List<TexeraTuple>();
                    }
                    Pair<bool, List<TexeraTuple>> pair = currentMap[currentSequenceNumber];
                    isEnd |= pair.First;
                    if(pair.Second!=null)
                    {
                        batchList.AddRange(pair.Second);
                    }
                    currentMap.Remove(currentSequenceNumber);
                    currentSequenceNumber++;
                    inSequenceNumberMap[sender]++;
                }
            }
        }

    }
}
