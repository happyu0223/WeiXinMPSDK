﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Senparc.Weixin.Cache;
using Senparc.Weixin.Cache.Redis;
using Senparc.Weixin.Containers;
using Senparc.Weixin.Helpers;
using Senparc.CO2NET.MessageQueue;
using Senparc.CO2NET.Cache;
using Senparc.CO2NET.Cache.Redis;
using Senparc.CO2NET.Trace;
using System.Threading.Tasks;

namespace Senparc.Weixin.Sample.NetCore3.Controllers
{
    [Serializable]
    internal class TestContainerBag1 : BaseContainerBag
    {
        //private DateTimeOffset _dateTime;

        public DateTimeOffset DateTime { get; set; }
        //{
        //    get { return _dateTime; }
        //    set { this.SetContainerProperty(ref _dateTime, value); }
        //}
    }

    //[Serializable]
    //internal class TestContainerBag2 : BaseContainerBag
    //{
    //    private DateTimeOffset _dateTime;
    //    public DateTimeOffset DateTime
    //    {
    //        get { return _dateTime; }
    //        set { this.SetContainerProperty(ref _dateTime, value); }

    //    }
    //}

    internal class TestContainer1 : BaseContainer<TestContainerBag1>
    {
    }

    //internal class TestContainer2 : BaseContainer<TestContainerBag2>
    //{
    //}

    /// <summary>
    /// 测试缓存
    /// </summary>
    public class CacheController : BaseController
    {
        public ActionResult Test()
        {
            return View();
        }

        public ActionResult Redis(int id = 1)
        {
            //测试Redis ItemCollection缓存更新功能

            var sb = new StringBuilder();
            if (id == 1)
            {
                sb.Append("使用Redis<br>");
                CacheStrategyFactory.RegisterObjectCacheStrategy(() => RedisObjectCacheStrategy.Instance);
            }
            else
            {
                sb.Append("使用本地缓存<br>");
                CacheStrategyFactory.RegisterObjectCacheStrategy(null);//注意：此处不能输入()=>null，这样仍然是一个有内容的委托！
            }


            //var cacheKey = TestContainer1.GetContainerCacheKey();
            var containerCacheStrategy = ContainerCacheStrategyFactory.GetContainerCacheStrategyInstance()/*.ContainerCacheStrategy*/;
            sb.AppendFormat("ContainerCacheStrategy：{0}<br />", containerCacheStrategy.GetType());

            var itemCollection = containerCacheStrategy.GetAll<TestContainerBag1>();

            sb.AppendFormat("Count1：{0}<br />", itemCollection != null ? itemCollection.Count() : -1);


            var bagKey = "Redis." + SystemTime.Now.ToString("yyyy-MM-dd--HH-mm-ss-ffff");
            var bag = new TestContainerBag1()
            {
                Key = bagKey,
                DateTime = SystemTime.Now
            };

            TestContainer1.Update(bagKey, bag, TimeSpan.FromHours(1));//更新到缓存（立即更新）

            itemCollection = containerCacheStrategy.GetAll<TestContainerBag1>();

            sb.AppendFormat("Count2：{0}<br />", itemCollection != null ? itemCollection.Count() : -1);

            if (itemCollection != null)
            {
                itemCollection[SystemTime.Now.Ticks.ToString()] = bag;
            }

            itemCollection = containerCacheStrategy.GetAll<TestContainerBag1>();//如果是分布式缓存，这里的数字通常不会变

            sb.AppendFormat("Count3：{0}<br />", itemCollection != null ? itemCollection.Count() : -1);

            return Content(sb.ToString(), "text/html; charset=utf-8");
        }


        #region 新方法

        [HttpPost]
        public async Task<IActionResult> RunTest()
        {
            var sb = new StringBuilder();

            try
            {
                var containerCacheStrategy = ContainerCacheStrategyFactory.GetContainerCacheStrategyInstance()/*.ContainerCacheStrategy*/;
                var currentSystemCacheStrategy = containerCacheStrategy.BaseCacheStrategy();
                sb.AppendFormat($"当前系统缓存策略：{containerCacheStrategy.GetType().Name}<br /><br />");
                var currentSystemContainerCacheStrategy = ContainerCacheStrategyFactory.GetContainerCacheStrategyInstance();
                sb.AppendFormat($"当前系统容器缓存（领域缓存）策略：{currentSystemContainerCacheStrategy.GetType().Name}<br /><br />");


                var caches = new Dictionary<IBaseObjectCacheStrategy, IContainerCacheStrategy> {
                                    { RedisObjectCacheStrategy.Instance, RedisContainerCacheStrategy.Instance },//如果没有配置 Redis，请注释掉这一句
                                    { LocalObjectCacheStrategy.Instance, LocalContainerCacheStrategy.Instance }
                                };
                var cacheExpire = TimeSpan.FromSeconds(1);

                foreach (var cacheSet in caches)
                {
                    bool testSuccess = false;
                    TestContainerBag1 cacheBag = null;
                    var baseCache = cacheSet.Key;
                    var containerCache = cacheSet.Value;
                    sb.AppendFormat($"====  开始指定调用缓存策略：{baseCache.GetType().Name} ====<br /><br />");

                    sb.Append($"----- 测试写入（先异步后同步） -----<br />");
                    var shortBagKey = SystemTime.Now.ToString("yyyyMMdd-HHmmss") + "." + cacheExpire.GetHashCode();//创建不重复的Key
                    var finalBagKey = baseCache.GetFinalKey(ContainerHelper.GetItemCacheKey(typeof(TestContainerBag1), shortBagKey));//获取最终缓存中的键
                    var bag = new TestContainerBag1()
                    {
                        Key = shortBagKey,
                        DateTime = SystemTime.Now
                    };
                    var dt1 = SystemTime.Now;
                    await baseCache.SetAsync(finalBagKey, bag, expiry: null, true);//不设置过期时间
                    sb.Append($"异步写入完成：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms<br />");

                    dt1 = SystemTime.Now;
                    baseCache.Set(finalBagKey, bag, expiry: null, true);//不设置过期时间
                    sb.Append($"同步写入完成：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms<br />");
                    sb.Append($"----- 写入测试完成 -----<br /><br />");

                    sb.Append($"----- 检查缓存读取（先同步后异步） -----<br />");


                    dt1 = SystemTime.Now;
                    cacheBag = baseCache.Get<TestContainerBag1>(finalBagKey, true);
                    sb.Append($"同步读取完成：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms<br />");

                    dt1 = SystemTime.Now;
                    cacheBag = await baseCache.GetAsync<TestContainerBag1>(finalBagKey, true);
                    testSuccess = cacheBag != null;
                    sb.Append($"异步读取完成：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms<br />");

                    testSuccess &= cacheBag != null && cacheBag.DateTime == bag.DateTime && bag.Key == shortBagKey;
                    sb.Append($"----- 检查结果：{(testSuccess ? "成功" : "失败")} -----<br /><br />");


                    sb.Append($"----- 容器缓存（领域缓存）修改测试 -----<br />");
                    dt1 = SystemTime.Now;
                    cacheBag.DateTime = SystemTime.Now;
                    await TestContainer1.UpdateAsync(cacheBag, cacheExpire);//设置过期时间
                    sb.Append($"写入完成：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms<br />");

                    var dt2 = SystemTime.Now;
                    var containerResult1 = await TestContainer1.TryGetItemAsync<TestContainerBag1>(shortBagKey, z => z);
                    sb.Append($"使用 ContainerCacheStrategy 读取完成：{SystemTime.NowDiff(dt2).TotalMilliseconds.ToString("f4")} ms<br />");

                    dt2 = SystemTime.Now;
                    var containerResult2 = await baseCache.GetAsync<TestContainerBag1>(finalBagKey, true);
                    sb.Append($"使用 BaseContainerStrategy 读取完成：{SystemTime.NowDiff(dt2).TotalMilliseconds.ToString("f4")} ms<br />");

                    testSuccess = containerResult1 != null && containerResult2 != null && containerResult1.Key == containerResult2.Key && containerResult1.DateTime == containerResult2.DateTime;
                    sb.Append($"----- 测试结果：{(testSuccess ? "成功" : "失败")}（总耗时：{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms） -----<br /><br />");

                    sb.Append($"----- 检查缓存过期 -----<br />");
                    var sleepTime = cacheExpire.Add(TimeSpan.FromMilliseconds(500));
                    sb.Append($"线程休眠：{sleepTime.TotalSeconds.ToString("f2")}s<br />");
                    await Task.Delay(sleepTime);
                    cacheBag = await baseCache.GetAsync<TestContainerBag1>(finalBagKey, true);
                    sb.Append($"----- 检查结果：{(cacheBag == null ? "成功" : "失败")} -----<br /><br />");

                    sb.Append($"----- 检查缓存删除 -----<br />");
                    await baseCache.UpdateAsync(finalBagKey, bag, cacheExpire, true);
                    cacheBag = await baseCache.GetAsync<TestContainerBag1>(finalBagKey, true);
                    testSuccess = cacheBag != null;
                    sb.Append($"写入待删除项目：{(cacheBag != null ? "成功" : "失败")}<br />");
                    dt1 = SystemTime.Now;
                    await baseCache.RemoveFromCacheAsync(finalBagKey, true);
                    sb.Append($"删除项目（{SystemTime.NowDiff(dt1).TotalMilliseconds.ToString("f4")} ms）<br />");
                    cacheBag = await baseCache.GetAsync<TestContainerBag1>(finalBagKey, true);
                    sb.Append($"----- 删除结果检查：{(cacheBag == null ? "成功" : "失败")} -----<br /><br /><br />");
                }

            }
            catch (Exception ex)
            {
                sb.Append($"===== 发生异常：{ex.Message} =====");
            }

            return Content(sb.ToString());
        }

        #endregion

        #region 旧方法（适用于老版本的容器缓存策略）

        //[HttpPost]
        //public ActionResult RunTest_Old()
        //{
        //    var sb = new StringBuilder();
        //    //var containerCacheStrategy = CacheStrategyFactory.GetContainerCacheStrategyInstance();
        //    var containerCacheStrategy = ContainerCacheStrategyFactory.GetContainerCacheStrategyInstance()/*.ContainerCacheStrategy*/;
        //    var baseCacheStrategy = containerCacheStrategy.BaseCacheStrategy();
        //    sb.AppendFormat("{0}：{1}<br />", "当前缓存策略", containerCacheStrategy.GetType().Name);

        //    var finalExisted = false;
        //    for (int i = 0; i < 3; i++)
        //    {
        //        sb.AppendFormat("<br />====== {0}：{1} ======<br /><br />", "开始一轮测试", i + 1);
        //        var shortBagKey = SystemTime.Now.ToString("yyyyMMdd-HHmmss");
        //        var finalBagKey = baseCacheStrategy.GetFinalKey(ContainerHelper.GetItemCacheKey(typeof(TestContainerBag1), shortBagKey));//获取最终缓存中的键
        //        var bag = new TestContainerBag1()
        //        {
        //            Key = shortBagKey,
        //            DateTime = SystemTime.Now
        //        };
        //        TestContainer1.Update(shortBagKey, bag, TimeSpan.FromHours(1)); //更新到缓存（立即更新）
        //        sb.AppendFormat("{0}：{1}<br />", "bag.DateTime", bag.DateTime.ToString("o"));

        //        Thread.Sleep(1);

        //        bag.DateTime = SystemTime.Now; //进行修改

        //        //读取队列
        //        var mq = new SenparcMessageQueue();
        //        var mqKey = SenparcMessageQueue.GenerateKey("ContainerBag", bag.GetType(), bag.Key, "UpdateContainerBag");
        //        var mqItem = mq.GetItem(mqKey);
        //        sb.AppendFormat("{0}：{1}<br />", "bag.DateTime", bag.DateTime.ToString("o"));
        //        sb.AppendFormat("{0}：{1}<br />", "已经加入队列", mqItem != null);
        //        sb.AppendFormat("{0}：{1}<br />", "当前消息队列数量（未更新缓存）", mq.GetCount());

        //        if (mq.GetCount() >= 3)
        //        {
        //            //超过3是不正常的，或有其他外部干扰
        //            sb.AppendFormat("<br>=====MQ队列（{0}）start=======<br>", mq.GetCount());
        //            foreach (var item in SenparcMessageQueue.MessageQueueDictionary)
        //            {
        //                sb.AppendFormat("{0}：{1}<br />", item.Key, item.Value.Key);
        //            }

        //            sb.AppendFormat("=====MQ队列（{0}）=======<br><br>", mq.GetCount());
        //        }

        //        var itemCollection = containerCacheStrategy.GetAll<TestContainerBag1>();

        //        var existed = itemCollection.ContainsKey(finalBagKey);
        //        sb.AppendFormat("{0}：{1}<br />", "当前缓存是否存在", existed);
        //        sb.AppendFormat("{0}：{1}<br />", "插入缓存时间",
        //            !existed ? "不存在" : itemCollection[finalBagKey].CacheTime.ToString("o")); //应为0

        //        var waitSeconds = i;
        //        sb.AppendFormat("{0}：{1}<br />", "操作", "等待" + waitSeconds + "秒");
        //        Thread.Sleep(waitSeconds * 1000); //队列线程默认轮询等待时间为1秒，当等待时间超过1秒时，应该都已经处理完毕

        //        sb.AppendFormat("{0}：{1}<br />", "当前消息队列数量（未更新缓存）", mq.GetCount());

        //        itemCollection = containerCacheStrategy.GetAll<TestContainerBag1>();
        //        existed = itemCollection.ContainsKey(finalBagKey);
        //        finalExisted = existed && itemCollection[finalBagKey].CacheTime.Date == SystemTime.Now.Date;
        //        sb.AppendFormat("{0}：{1}<br />", "当前缓存是否存在", existed);
        //        sb.AppendFormat("{0}：{1}<br />", "插入缓存时间",
        //            !existed ? "不存在" : itemCollection[finalBagKey].CacheTime.ToString("o")); //应为当前加入到缓存的最新时间

        //    }

        //    sb.AppendFormat("<br />============<br /><br />");
        //    sb.AppendFormat("{0}：{1}<br />", "测试结果", !finalExisted ? "失败" : "成功");

        //    return Content(sb.ToString());
        //    //ViewData["Result"] = sb.ToString();
        //    //return View();
        //}


        #endregion
    }
}